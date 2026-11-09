using System.Reflection;

namespace LanFlow.Desktop.Models;

/// <summary>
/// Settings 字段流转的唯一实现：复制、深拷贝与归一化。
/// 契约对齐 Windows 主线 v2.0.1 的 SettingsPipeline：反射遍历全部公共可读写属性，
/// 新增设置字段自动进入保存链路，不再依赖人工字段清单。
/// Windows 1.6.1（漏 OcrEnabled）与 1.6.2（漏 ScreenshotHotkey）两次「设置保存后不落盘」
/// 皆因手工清单漏项；Linux 端此前 Clone / ApplyAppearance / ConfigStore.Normalize 同样有三份清单，
/// 由结构本身消除该风险。
/// 职责分离：CopyAll 只搬运、Clone 负责深拷贝、Normalize 负责夹紧/枚举/迁移/派生。
/// </summary>
public static class SettingsPipeline
{
    /// <summary>运行态字段：由运行逻辑维护，设置窗口不得覆盖。Linux 端当前没有这类字段，保留扩展点。</summary>
    public static readonly string[] RuntimeOnlyProperties = [];

    private static readonly PropertyInfo[] WritableProperties = typeof(Settings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .ToArray();

    /// <summary>供完备性测试遍历；新增属性自动进入测试覆盖，无需改动测试代码。</summary>
    public static IReadOnlyList<PropertyInfo> Writable => WritableProperties;

    public static Settings Clone(Settings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = new Settings();
        CopyAll(source, copy);
        // 引用类型必须深拷贝，否则设置预览会直接改动当前生效的主题。
        copy.ThemeColors = source.ThemeColors?.Clone() ?? ThemeColors.Dark();
        copy.CustomThemes = source.CustomThemes?
            .Select(theme => new ThemeProfile
            {
                Name = theme.Name,
                Colors = theme.Colors?.Clone() ?? ThemeColors.Dark(),
            })
            .ToList() ?? [];
        return copy;
    }

    /// <summary>
    /// 复制全部可写属性。设置窗口的保存链路应传 skipRuntimeOnly: true，
    /// 保留目标上由运行逻辑维护的字段。
    /// 与 Windows v2.0.1 一致：此处只做浅搬运，深拷贝是 Clone 的职责。
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
    /// 夹紧、枚举归一化、空值兜底与派生字段。与复制解耦，测试可对同一份输入独立计算期望值。
    /// ConfigStore.Load 与 MainViewModel.ApplyAppearance 共用这一份口径，
    /// 避免原先「加载时夹紧一部分、设置页夹紧另一部分」的规则分叉。
    ///
    /// 与 Windows v2.0.1 的差异（Linux 平台适配，刻意保留）：
    /// Linux 的模型默认热键仍是 Alt+Space，而 X11 窗口管理器普遍占用该组合；
    /// 因此这里保留「空值 / 旧默认值 → 平台默认热键」的迁移，由 defaultHotkey 注入。
    /// 加载与设置页保存都经过此处，避免「保存即时生效、重启后才被悄悄改写」。
    /// </summary>
    public static void Normalize(Settings settings, string defaultHotkey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(defaultHotkey))
            throw new ArgumentException("平台默认热键不能为空。", nameof(defaultHotkey));

        if (string.IsNullOrWhiteSpace(settings.Hotkey) ||
            settings.Hotkey.Equals("Alt+Space", StringComparison.OrdinalIgnoreCase))
        {
            settings.Hotkey = defaultHotkey;
        }

        settings.Theme = settings.Theme == "light" ? "light" : "dark";
        settings.ThemeProfile = string.IsNullOrWhiteSpace(settings.ThemeProfile)
            ? (settings.Theme == "light" ? "浅色" : "深色")
            : settings.ThemeProfile;
        settings.ThemeColors ??= settings.Theme == "light" ? ThemeColors.Light() : ThemeColors.Dark();
        settings.CustomThemes ??= [];

        settings.LayoutMode = settings.LayoutMode == "card" ? "card" : "tile";
        settings.GroupLayout = settings.GroupLayout == "top" ? "top" : "left";
        settings.GroupSwitchMode = settings.GroupSwitchMode == "hover" ? "hover" : "click";
        settings.AnimationMode = settings.AnimationMode == "off" ? "off" : "on";
        settings.TransparencyMode = settings.TransparencyMode == "wholeWindow" ? "wholeWindow" : "layered";

        settings.LayeredOpacity = Math.Clamp(settings.LayeredOpacity, 0.55, 1.0);
        settings.WholeWindowOpacity = Math.Clamp(settings.WholeWindowOpacity, 0.55, 1.0);
        // 派生字段：渲染实际读取 LayeredOpacity / WholeWindowOpacity，
        // Opacity 只是滑块初值与旧配置兼容位，跟随当前模式保持一致（对齐 v2.0.1）。
        settings.Opacity = settings.TransparencyMode == "wholeWindow"
            ? settings.WholeWindowOpacity
            : settings.LayeredOpacity;

        settings.IconSize = Math.Clamp(settings.IconSize, 24, 72);
        settings.CardWidth = Math.Clamp(settings.CardWidth, 48, 320);
        settings.CardHeight = Math.Clamp(settings.CardHeight, 48, 240);
        settings.CardSize = Math.Clamp(settings.CardSize, 76, 160);
        settings.TextSize = Math.Clamp(settings.TextSize, 10, 18);
        settings.ItemSpacing = Math.Clamp(settings.ItemSpacing, 0, 64);
        settings.RowSpacing = Math.Clamp(settings.RowSpacing, 0, 80);
        settings.ContentPadding = Math.Clamp(settings.ContentPadding, 6, 40);
        settings.GroupHoverDelayMs = Math.Clamp(settings.GroupHoverDelayMs, 0, 1000);
    }
}
