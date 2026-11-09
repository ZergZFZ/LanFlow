using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FlickD.Core.Models;
using FlickD.Desktop.Models;
using FlickD.Desktop.Services;
using FlickD.Desktop.ViewModels;

namespace FlickD.Core.Tests;

/// <summary>
/// 设置保存链路的字段完备性契约。复制、深拷贝与归一化已集中到 SettingsPipeline：
/// 反射遍历全部可写属性，新增 Settings 字段自动被复制，也自动进入测试覆盖，
/// 不再依赖逐字段登记的异值表。1.6.1 / 1.6.2 的「设置不落盘」源于手工清单漏项，
/// 由结构本身消除，而不是靠人记得补字典。
/// </summary>
public sealed class ApplyAppearanceCompletenessTests
{
    private static bool IsReferenceProperty(PropertyInfo property) =>
        property.Name is nameof(Settings.ThemeColors) or nameof(Settings.CustomThemes);

    public static IEnumerable<object[]> WritableSettingsProperties()
    {
        var properties = SettingsPipeline.Writable
            .Where(p => p.Name != nameof(Settings.LastGroupId) && !IsReferenceProperty(p))
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
        var source = config.Settings.Clone();

        property.SetValue(source, ProbeValue(property, property.GetValue(source)));

        // 期望值由与生产代码同一套 Normalize 规则独立计算。
        var expectedSettings = SettingsPipeline.Clone(source);
        SettingsPipeline.Normalize(expectedSettings);
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
        Assert.NotSame(source.ThemeColors, clone.ThemeColors);
        Assert.NotSame(source.CustomThemes, clone.CustomThemes);
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
    public void ApplyAppearance_LeavesRuntimeLastGroupIdUntouched()
    {
        var config = new AppConfig();
        config.Settings.LastGroupId = "group-runtime";
        var viewModel = new MainViewModel(new RecordingStore(config));
        var source = config.Settings.Clone();
        source.LastGroupId = "group-from-settings-window";

        viewModel.ApplyAppearance(source, persist: false);

        Assert.Equal("group-runtime", viewModel.Settings.LastGroupId);
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

    // 与当前值明显不同的探针值；归一化字段的期望由 Normalize 计算，探针值无需落在合法域内。
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
        null => "target-value",
        _ => throw new NotSupportedException($"属性 {property.Name} 类型 {property.PropertyType} 缺少回退值规则"),
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
