using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FlickD.Desktop.Models;

namespace FlickD.Core.Models;

/// <summary>
/// Settings 字段流转的唯一实现：复制、深拷贝与归一化。
/// 通过反射遍历全部公共可读写属性，新增设置字段自动纳入，避免
/// 1.6.1（OcrEnabled / OcrShowResultWindow）与 1.6.2（ScreenshotHotkey / GroupHoverDelayMs）
/// 因人工维护的字段清单漏项，再次出现「设置保存后不落盘」。
/// </summary>
public static class SettingsPipeline
{
    /// <summary>运行态字段：由选中分组逻辑维护，设置窗口不得覆盖。</summary>
    public static readonly string[] RuntimeOnlyProperties = [nameof(Settings.LastGroupId)];

    private static readonly PropertyInfo[] WritableProperties = typeof(Settings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .ToArray();

    /// <summary>供完备性测试遍历；新增属性无需改动测试代码。</summary>
    public static IReadOnlyList<PropertyInfo> Writable => WritableProperties;

    public static Settings Clone(Settings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = new Settings();
        CopyAll(source, copy);
        // 引用类型必须深拷贝，否则设置预览会直接改动当前生效的主题。
        copy.ThemeColors = source.ThemeColors.Clone();
        copy.CustomThemes = source.CustomThemes
            .Select(theme => new ThemeProfile { Name = theme.Name, Colors = theme.Colors.Clone() })
            .ToList();
        return copy;
    }

    /// <summary>
    /// 复制全部可写属性。设置窗口的保存链路应传 skipRuntimeOnly: true，
    /// 保留目标上由运行逻辑维护的字段。
    /// </summary>
    public static void CopyAll(Settings source, Settings target, bool skipRuntimeOnly = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        foreach (var property in WritableProperties)
        {
            if (skipRuntimeOnly && RuntimeOnlyProperties.Contains(property.Name, StringComparer.Ordinal))
                continue;
            property.SetValue(target, property.GetValue(source));
        }
    }

    /// <summary>
    /// 夹紧、枚举归一化与派生字段。与复制解耦，测试可对同一份输入独立计算期望值。
    /// 这里只做业务规则；字段复制由 <see cref="CopyAll"/> 统一负责。
    /// </summary>
    public static void Normalize(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Theme = settings.Theme == "light" ? "light" : "dark";
        settings.LayoutMode = settings.LayoutMode == SettingsOptionValues.CardLayout
            ? SettingsOptionValues.CardLayout
            : SettingsOptionValues.GridLayout;
        settings.IconSize = Math.Clamp(settings.IconSize, 24, 72);
        settings.CardWidth = Math.Clamp(settings.CardWidth, 48, 320);
        settings.CardHeight = Math.Clamp(settings.CardHeight, 48, 240);
        settings.CardSize = Math.Clamp(settings.CardSize, 76, 160);
        settings.TextSize = Math.Clamp(settings.TextSize, 10, 18);
        settings.ItemSpacing = Math.Clamp(settings.ItemSpacing, 0, 64);
        settings.RowSpacing = Math.Clamp(settings.RowSpacing, 0, 80);
        settings.ContentPadding = Math.Clamp(settings.ContentPadding, 6, 40);
        settings.GroupLayout = settings.GroupLayout == SettingsOptionValues.GroupTop
            ? SettingsOptionValues.GroupTop
            : SettingsOptionValues.GroupLeft;
        settings.GroupSwitchMode = settings.GroupSwitchMode == SettingsOptionValues.GroupSwitchHover
            ? SettingsOptionValues.GroupSwitchHover
            : SettingsOptionValues.GroupSwitchClick;
        settings.GroupLabelSize = Math.Clamp(settings.GroupLabelSize, 28, 52);
        settings.GroupLabelFontSize = Math.Clamp(settings.GroupLabelFontSize, 11, 18);
        settings.GroupNavigationWidth = Math.Clamp(settings.GroupNavigationWidth, 96, 280);
        settings.TransparencyMode = settings.TransparencyMode == SettingsOptionValues.TransparencyWholeWindow
            ? SettingsOptionValues.TransparencyWholeWindow
            : SettingsOptionValues.TransparencyLayered;
        settings.LayeredOpacity = Math.Clamp(settings.LayeredOpacity, 0.40, 1.00);
        settings.WholeWindowOpacity = Math.Clamp(settings.WholeWindowOpacity, 0.40, 1.00);
        settings.Opacity = settings.TransparencyMode == SettingsOptionValues.TransparencyWholeWindow
            ? settings.WholeWindowOpacity
            : settings.LayeredOpacity;
        settings.AnimationMode = settings.AnimationMode is SettingsOptionValues.AnimationOn or SettingsOptionValues.AnimationOff
            ? settings.AnimationMode
            : SettingsOptionValues.AnimationSystem;
        settings.GroupHoverDelayMs = Math.Clamp(settings.GroupHoverDelayMs, 0, SettingsOptionValues.MaxGroupHoverDelayMs);
    }
}
