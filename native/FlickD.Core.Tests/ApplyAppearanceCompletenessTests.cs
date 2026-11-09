using System.Reflection;
using FlickD.Desktop.Models;
using FlickD.Desktop.Services;
using FlickD.Desktop.ViewModels;

namespace FlickD.Core.Tests;

/// <summary>
/// ApplyAppearance 是设置窗口"保存"链路的核心复制边界。1.6.1 曾因漏复制
/// OcrEnabled/OcrShowResultWindow 导致开关不落盘；本测试反射遍历 Settings
/// 全部公共可写属性，保证今后新增字段漏处理时测试直接失败。
/// 取值均为合法域内异值：ApplyAppearance 对越界值做夹紧/归一化属预期行为，
/// 本测试的契约是"合法输入必须原样传递"。
/// </summary>
public sealed class ApplyAppearanceCompletenessTests
{
    // ApplyAppearance 有意不处理的字段：
    // - LastGroupId：运行态字段，由选中分组逻辑维护，不能被设置窗口覆盖。
    // - Opacity：由 TransparencyMode/LayeredOpacity/WholeWindowOpacity 派生。
    // - ThemeColors / CustomThemes：按引用整体赋值，用专门的引用一致性用例覆盖。
    private static readonly HashSet<string> ExcludedProperties =
    [
        nameof(Settings.LastGroupId),
        nameof(Settings.Opacity),
        nameof(Settings.ThemeColors),
        nameof(Settings.CustomThemes),
    ];

    // 每个属性的合法域内异值。新增 Settings 属性若没有条目，测试会失败并提示补充。
    private static readonly Dictionary<string, Func<object?, object?>> DistinctValues = new()
    {
        [nameof(Settings.Hotkey)] = _ => "Alt+Q",
        [nameof(Settings.ScreenshotHotkey)] = _ => "Ctrl+Shift+Q",
        [nameof(Settings.Theme)] = _ => "light",
        [nameof(Settings.ThemeProfile)] = _ => "浅色方案",
        [nameof(Settings.LayoutMode)] = _ => SettingsOptionValues.CardLayout,
        [nameof(Settings.IconSize)] = _ => 55.0,
        [nameof(Settings.CardWidth)] = _ => 200.0,
        [nameof(Settings.CardHeight)] = _ => 180.0,
        [nameof(Settings.CardSize)] = _ => 140.0,
        [nameof(Settings.TextSize)] = _ => 16.0,
        [nameof(Settings.ItemSpacing)] = _ => 40.0,
        [nameof(Settings.RowSpacing)] = _ => 60.0,
        [nameof(Settings.ContentPadding)] = _ => 30.0,
        [nameof(Settings.ShowShortcutBadge)] = _ => true,
        [nameof(Settings.ShowFullItemName)] = _ => true,
        [nameof(Settings.ShowItemTitle)] = _ => false,
        [nameof(Settings.GroupLayout)] = _ => SettingsOptionValues.GroupTop,
        [nameof(Settings.GroupSwitchMode)] = _ => SettingsOptionValues.GroupSwitchHover,
        [nameof(Settings.GroupLabelSize)] = _ => 48.0,
        [nameof(Settings.GroupLabelFontSize)] = _ => 17.0,
        [nameof(Settings.GroupNavigationWidth)] = _ => 240.0,
        [nameof(Settings.TransparencyMode)] = _ => SettingsOptionValues.TransparencyWholeWindow,
        [nameof(Settings.LayeredOpacity)] = _ => 0.65,
        [nameof(Settings.WholeWindowOpacity)] = _ => 0.65,
        [nameof(Settings.AnimationMode)] = _ => SettingsOptionValues.AnimationOn,
        [nameof(Settings.GroupTransitionAnimation)] = _ => true,
        [nameof(Settings.StartWithWindows)] = _ => true,
        [nameof(Settings.OpenItemsOnSingleClick)] = _ => false,
        [nameof(Settings.GroupHoverDelayMs)] = _ => 260,
        [nameof(Settings.OcrEnabled)] = _ => true,
        [nameof(Settings.OcrShowResultWindow)] = _ => true,
    };

    public static IEnumerable<object[]> WritableSettingsProperties()
    {
        var properties = typeof(Settings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(properties);

        foreach (var property in properties)
        {
            if (!ExcludedProperties.Contains(property.Name))
            {
                yield return [property.Name, property];
            }
        }
    }

    [Theory]
    [MemberData(nameof(WritableSettingsProperties))]
    public void ApplyAppearance_CopiesEveryWritableSettingsProperty(string propertyName, PropertyInfo property)
    {
        Assert.True(
            DistinctValues.ContainsKey(propertyName),
            $"Settings 新增属性 {propertyName} 尚未登记异值规则，请在 {nameof(DistinctValues)} 中补充");

        var config = new AppConfig();
        var store = new RecordingStore(config);
        var viewModel = new MainViewModel(store);

        var source = config.Settings.Clone();
        var expected = DistinctValues[propertyName](property.GetValue(source));
        property.SetValue(source, expected);
        // 目标先取与 source 不同的值，避免默认相等造成假阳性。
        var current = property.GetValue(config.Settings);
        if (Equals(current, expected))
        {
            property.SetValue(config.Settings, DefaultBackValue(propertyName, current));
        }

        viewModel.ApplyAppearance(source, persist: false);

        var actual = property.GetValue(viewModel.Settings);
        Assert.True(
            Equals(expected, actual),
            $"ApplyAppearance 未复制属性 {propertyName}（期望 {Format(expected)}，实际 {Format(actual)}）");
    }

    [Fact]
    public void ApplyAppearance_AssignsThemeColorsByReference()
    {
        var config = new AppConfig();
        var store = new RecordingStore(config);
        var viewModel = new MainViewModel(store);

        var source = config.Settings.Clone();
        var colors = new ThemeColors { Panel = "#010203", Accent = "#040506" };
        source.ThemeColors = colors;
        var themes = new List<ThemeProfile> { new() { Name = "方案甲" } };
        source.CustomThemes = themes;

        viewModel.ApplyAppearance(source, persist: false);

        Assert.Same(colors, viewModel.Settings.ThemeColors);
        Assert.Same(themes, viewModel.Settings.CustomThemes);
        Assert.Equal("#010203", viewModel.Settings.ThemeColors.Panel);
    }

    [Fact]
    public void ApplyAppearance_WithPersist_PersistsKeyAndOcrFields()
    {
        var config = new AppConfig();
        var store = new RecordingStore(config);
        var viewModel = new MainViewModel(store);
        var source = config.Settings.Clone();

        source.ScreenshotHotkey = "Ctrl+Shift+Q";
        source.GroupHoverDelayMs = 260;
        source.OcrEnabled = true;
        source.OcrShowResultWindow = true;
        source.Hotkey = "Alt+Q";

        viewModel.ApplyAppearance(source, persist: true);

        var saved = store.LastSavedConfig!.Settings;
        Assert.Equal("Ctrl+Shift+Q", saved.ScreenshotHotkey);
        Assert.Equal(260, saved.GroupHoverDelayMs);
        Assert.True(saved.OcrEnabled);
        Assert.True(saved.OcrShowResultWindow);
        Assert.Equal("Alt+Q", saved.Hotkey);
        Assert.Equal(1, store.SaveCallCount);
    }

    // 目标与期望撞值时的回退取值（保证目标初始 ≠ 期望）。
    private static object? DefaultBackValue(string propertyName, object? current) => propertyName switch
    {
        nameof(Settings.Theme) => "dark",
        nameof(Settings.LayoutMode) => SettingsOptionValues.GridLayout,
        nameof(Settings.GroupLayout) => SettingsOptionValues.GroupLeft,
        nameof(Settings.GroupSwitchMode) => SettingsOptionValues.GroupSwitchClick,
        nameof(Settings.TransparencyMode) => SettingsOptionValues.TransparencyLayered,
        nameof(Settings.AnimationMode) => SettingsOptionValues.AnimationOff,
        _ when current is bool b => !b,
        _ when current is double d => Math.Clamp(d + 1, 0.40, 500),
        _ when current is int i => Math.Clamp(i - 1, 0, 500),
        _ => throw new NotSupportedException($"属性 {propertyName} 缺少回退取值规则"),
    };

    private static string Format(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? string.Empty,
    };

    private sealed class RecordingStore(AppConfig loadedConfig) : IConfigStore
    {
        public int SaveCallCount { get; private set; }
        public AppConfig? LastSavedConfig { get; private set; }

        public AppConfig Load() => loadedConfig;

        public void Save(AppConfig config)
        {
            SaveCallCount++;
            LastSavedConfig = config;
        }
    }
}
