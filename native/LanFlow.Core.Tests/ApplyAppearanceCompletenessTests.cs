using System.Reflection;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.ViewModels;

namespace LanFlow.Core.Tests;

/// <summary>
/// 设置保存链路的字段完备性契约（移植自 Windows 主线 v2.0.1）。
/// 复制、深拷贝与归一化集中到 SettingsPipeline 后，反射遍历全部可写属性：
/// 新增 Settings 字段自动被复制，也自动进入测试覆盖，不再依赖逐字段登记的异值表。
/// 1.6.1 / 1.6.2 的「设置不落盘」源于手工清单漏项，由结构本身消除。
/// </summary>
public sealed class ApplyAppearanceCompletenessTests
{
    private static bool IsReferenceProperty(PropertyInfo property) =>
        property.Name is nameof(Settings.ThemeColors) or nameof(Settings.CustomThemes);

    public static IEnumerable<object[]> WritableSettingsProperties()
    {
        var properties = SettingsPipeline.Writable
            .Where(p => !IsReferenceProperty(p) &&
                        !SettingsPipeline.RuntimeOnlyProperties.Contains(p.Name, StringComparer.Ordinal))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(properties);

        foreach (var property in properties)
            yield return [property.Name, property];
    }

    [Theory]
    [MemberData(nameof(WritableSettingsProperties))]
    public void ApplyAppearance_CopiesEveryWritableSettingsProperty(string propertyName, PropertyInfo property)
    {
        var config = new AppConfig();
        var store = new RecordingStore(config);
        var viewModel = new MainViewModel(store);
        store.Reset();

        var source = config.Settings.Clone();
        property.SetValue(source, ProbeValue(property, property.GetValue(source)));

        // 期望值由与生产代码同一套 Normalize 规则独立计算。
        var expectedSettings = SettingsPipeline.Clone(source);
        SettingsPipeline.Normalize(expectedSettings, store.DefaultHotkey);
        var expected = property.GetValue(expectedSettings);

        // 目标先取不同值，避免「完全没复制」也恰好相等的假阳性。
        property.SetValue(config.Settings, DifferentValue(property, expected));

        viewModel.ApplyAppearance(source, persist: false);

        var actual = property.GetValue(viewModel.Settings);
        Assert.True(
            Equals(expected, actual),
            $"ApplyAppearance 未正确复制属性 {propertyName}（期望 {Format(expected)}，实际 {Format(actual)}）");
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public void Clone_CopiesEveryWritableSettingsProperty()
    {
        var source = new Settings();
        foreach (var property in SettingsPipeline.Writable)
        {
            if (IsReferenceProperty(property)) continue;
            property.SetValue(source, ProbeValue(property, property.GetValue(source)));
        }

        var clone = source.Clone();

        foreach (var property in SettingsPipeline.Writable)
        {
            if (IsReferenceProperty(property)) continue;
            Assert.Equal(property.GetValue(source), property.GetValue(clone));
        }
    }

    [Fact]
    public void Clone_DeepCopiesThemeColorsAndCustomThemes()
    {
        var source = new Settings
        {
            ThemeColors = new ThemeColors { Panel = "#010203", Accent = "#040506" },
            CustomThemes = [new ThemeProfile { Name = "方案甲", Colors = new ThemeColors { Panel = "#AABBCC" } }],
        };

        var clone = source.Clone();

        Assert.NotSame(source.ThemeColors, clone.ThemeColors);
        Assert.NotSame(source.CustomThemes, clone.CustomThemes);
        Assert.NotSame(source.CustomThemes[0], clone.CustomThemes[0]);
        Assert.NotSame(source.CustomThemes[0].Colors, clone.CustomThemes[0].Colors);
        Assert.Equal("#010203", clone.ThemeColors.Panel);
        Assert.Equal("#AABBCC", clone.CustomThemes[0].Colors.Panel);

        // 修改副本不得影响当前生效的主题（设置预览取消时的语义）。
        clone.ThemeColors.Panel = "#FFFFFF";
        clone.CustomThemes[0].Colors.Panel = "#000000";
        Assert.Equal("#010203", source.ThemeColors.Panel);
        Assert.Equal("#AABBCC", source.CustomThemes[0].Colors.Panel);
    }

    [Fact]
    public void ApplyAppearance_AssignsReferenceSettingsByReference()
    {
        var config = new AppConfig();
        var store = new RecordingStore(config);
        var viewModel = new MainViewModel(store);
        store.Reset();

        var source = config.Settings.Clone();
        var colors = new ThemeColors { Panel = "#010203" };
        var themes = new List<ThemeProfile> { new() { Name = "方案甲" } };
        source.ThemeColors = colors;
        source.CustomThemes = themes;

        viewModel.ApplyAppearance(source, persist: false);

        Assert.Same(colors, viewModel.Settings.ThemeColors);
        Assert.Same(themes, viewModel.Settings.CustomThemes);
        Assert.Equal("#010203", viewModel.Settings.ThemeColors.Panel);
    }

    [Fact]
    public void ApplyAppearance_WithPersist_PersistsHotkeyAndInteractionFields()
    {
        var config = new AppConfig();
        var store = new RecordingStore(config);
        var viewModel = new MainViewModel(store);
        store.Reset();

        var source = config.Settings.Clone();
        source.Hotkey = "Ctrl+Alt+K";
        source.HideOnDeactivate = true;
        source.GroupSwitchMode = "hover";
        source.GroupHoverDelayMs = 260;
        source.AnimationMode = "off";
        source.TransparencyMode = "wholeWindow";
        source.WholeWindowOpacity = 0.72;

        viewModel.ApplyAppearance(source, persist: true);

        var saved = store.LastSavedConfig!.Settings;
        Assert.Equal("Ctrl+Alt+K", saved.Hotkey);
        Assert.True(saved.HideOnDeactivate);
        Assert.Equal("hover", saved.GroupSwitchMode);
        Assert.Equal(260, saved.GroupHoverDelayMs);
        Assert.Equal("off", saved.AnimationMode);
        Assert.Equal("wholeWindow", saved.TransparencyMode);
        Assert.Equal(0.72, saved.WholeWindowOpacity);
        // 派生字段：整窗模式下 Opacity 跟随 WholeWindowOpacity（对齐 v2.0.1）。
        Assert.Equal(0.72, saved.Opacity);
        Assert.Equal(1, store.SaveCallCount);
    }

    [Fact]
    public void ApplyAppearance_MigratesLegacyAltSpaceHotkeyImmediately()
    {
        var config = new AppConfig();
        var store = new RecordingStore(config);
        var viewModel = new MainViewModel(store);
        store.Reset();

        var source = config.Settings.Clone();
        source.Hotkey = "Alt+Space";

        viewModel.ApplyAppearance(source, persist: false);

        // 保存与加载共用同一归一化口径，避免「保存生效、重启后变回默认」。
        Assert.Equal(store.DefaultHotkey, viewModel.Settings.Hotkey);
    }

    private static object ProbeValue(PropertyInfo property, object? current) => current switch
    {
        bool b => !b,
        double d => d + 7.5,
        int i => i + 37,
        string s => s + "-probe",
        null when property.PropertyType == typeof(string) => "probe-value",
        _ => throw new NotSupportedException($"属性 {property.Name} 类型 {property.PropertyType} 缺少探针值规则"),
    };

    private static object DifferentValue(PropertyInfo property, object? expected) => expected switch
    {
        bool b => !b,
        double d => d + 3.25,
        int i => i + 11,
        string s => s + "-target",
        _ => throw new NotSupportedException($"属性 {property.Name} 类型 {property.PropertyType} 缺少异值规则"),
    };

    private static string Format(object? value) => value?.ToString() ?? "<null>";
}
