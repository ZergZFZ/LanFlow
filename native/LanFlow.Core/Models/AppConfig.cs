using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace LanFlow.Desktop.Models;

public sealed class AppConfig
{
    [JsonPropertyName("groups")]
    public ObservableCollection<Group> Groups { get; set; } = [];

    [JsonPropertyName("settings")]
    public Settings Settings { get; set; } = new();
}

public sealed class Group
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "未命名分组";

    [JsonPropertyName("collapsed")]
    public bool Collapsed { get; set; }

    [JsonPropertyName("items")]
    public ObservableCollection<LauncherItem> Items { get; set; } = [];

    [JsonPropertyName("sortMode")]
    public string SortMode { get; set; } = "custom";
}

public sealed class LauncherItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "未命名项目";

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "app";

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = string.Empty;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("useCount")]
    public long UseCount { get; set; }

    [JsonIgnore]
    public string DisplayName => Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
        ? Name[..^4]
        : Name.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? Name[..^8] : Name;

    [JsonIgnore]
    public bool IsCommand => Kind == "command";

    // 平台无关的图标承载：Desktop 塞 System.Windows.Media.ImageSource，Linux 塞 Avalonia.Media.IImage。
    [JsonIgnore]
    public object? IconImage { get; set; }
}

public sealed class Settings
{
    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "Alt+Space";

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "dark";

    [JsonPropertyName("themeProfile")]
    public string ThemeProfile { get; set; } = "深色";

    [JsonPropertyName("themeColors")]
    public ThemeColors ThemeColors { get; set; } = ThemeColors.Dark();

    [JsonPropertyName("customThemes")]
    public List<ThemeProfile> CustomThemes { get; set; } = [];

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1.0;

    [JsonPropertyName("layoutMode")]
    public string LayoutMode { get; set; } = "tile";

    [JsonPropertyName("iconSize")]
    public double IconSize { get; set; } = 44;

    [JsonPropertyName("cardWidth")]
    public double CardWidth { get; set; } = 108;

    [JsonPropertyName("cardHeight")]
    public double CardHeight { get; set; } = 96;

    [JsonPropertyName("cardSize")]
    public double CardSize { get; set; } = 108;

    [JsonPropertyName("textSize")]
    public double TextSize { get; set; } = 12;

    [JsonPropertyName("itemSpacing")]
    public double ItemSpacing { get; set; } = 8;

    [JsonPropertyName("rowSpacing")]
    public double RowSpacing { get; set; } = 8;

    [JsonPropertyName("contentPadding")]
    public double ContentPadding { get; set; } = 16;

    [JsonPropertyName("showShortcutBadge")]
    public bool ShowShortcutBadge { get; set; }

    [JsonPropertyName("showFullItemName")]
    public bool ShowFullItemName { get; set; }

    [JsonPropertyName("showItemTitle")]
    public bool ShowItemTitle { get; set; } = true;

    [JsonPropertyName("groupLayout")]
    public string GroupLayout { get; set; } = "left";

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    [JsonPropertyName("openItemsOnSingleClick")]
    public bool OpenItemsOnSingleClick { get; set; } = true;

    // ---- B2 交互体验新增（Linux 对齐 Windows 基线）----
    [JsonPropertyName("hideOnDeactivate")]
    public bool HideOnDeactivate { get; set; }

    [JsonPropertyName("groupSwitchMode")]
    public string GroupSwitchMode { get; set; } = "click";

    [JsonPropertyName("groupHoverDelayMs")]
    public int GroupHoverDelayMs { get; set; } = 300;

    [JsonPropertyName("animationMode")]
    public string AnimationMode { get; set; } = "on";

    // ---- B3 透明度双模式（对齐 Windows：分层/整窗）----
    [JsonPropertyName("transparencyMode")]
    public string TransparencyMode { get; set; } = "layered";

    [JsonPropertyName("layeredOpacity")]
    public double LayeredOpacity { get; set; } = 1.0;

    [JsonPropertyName("wholeWindowOpacity")]
    public double WholeWindowOpacity { get; set; } = 0.85;

    public Settings Clone() => new()
    {
        Hotkey = Hotkey,
        Theme = Theme,
        ThemeProfile = ThemeProfile,
        ThemeColors = ThemeColors.Clone(),
        CustomThemes = CustomThemes.Select(c => new ThemeProfile { Name = c.Name, Colors = c.Colors.Clone() }).ToList(),
        Opacity = Opacity,
        LayoutMode = LayoutMode,
        IconSize = IconSize,
        CardWidth = CardWidth,
        CardHeight = CardHeight,
        CardSize = CardSize,
        TextSize = TextSize,
        ItemSpacing = ItemSpacing,
        RowSpacing = RowSpacing,
        ContentPadding = ContentPadding,
        ShowShortcutBadge = ShowShortcutBadge,
        ShowFullItemName = ShowFullItemName,
        ShowItemTitle = ShowItemTitle,
        GroupLayout = GroupLayout,
        StartWithWindows = StartWithWindows,
        OpenItemsOnSingleClick = OpenItemsOnSingleClick,
        HideOnDeactivate = HideOnDeactivate,
        GroupSwitchMode = GroupSwitchMode,
        GroupHoverDelayMs = GroupHoverDelayMs,
        AnimationMode = AnimationMode,
        TransparencyMode = TransparencyMode,
        LayeredOpacity = LayeredOpacity,
        WholeWindowOpacity = WholeWindowOpacity,
    };
}

public sealed class ThemeProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "自定义风格";

    [JsonPropertyName("colors")]
    public ThemeColors Colors { get; set; } = ThemeColors.Dark();
}

public sealed class ThemeColors
{
    [JsonPropertyName("panel")] public string Panel { get; set; } = "#171B28";
    [JsonPropertyName("panelBorder")] public string PanelBorder { get; set; } = "#343B50";
    [JsonPropertyName("surface")] public string Surface { get; set; } = "#22283A";
    [JsonPropertyName("surfaceBorder")] public string SurfaceBorder { get; set; } = "#38425B";
    [JsonPropertyName("footer")] public string Footer { get; set; } = "#1D2231";
    [JsonPropertyName("textPrimary")] public string TextPrimary { get; set; } = "#F5F7FC";
    [JsonPropertyName("textSecondary")] public string TextSecondary { get; set; } = "#ADB5C7";
    [JsonPropertyName("accent")] public string Accent { get; set; } = "#35405E";
    [JsonPropertyName("hover")] public string Hover { get; set; } = "#2B3247";
    [JsonPropertyName("iconSurface")] public string IconSurface { get; set; } = "#2A3040";

    public static ThemeColors Dark() => new();

    public static ThemeColors Light() => new()
    {
        Panel = "#F6F7FB", PanelBorder = "#CCD2E0", Surface = "#FFFFFF", SurfaceBorder = "#D7DCE8",
        Footer = "#EEF1F7", TextPrimary = "#1E2533", TextSecondary = "#59657A", Accent = "#DCE7FA",
        Hover = "#E9EDF5", IconSurface = "#E2E6ED",
    };

    public ThemeColors Clone() => new()
    {
        Panel = Panel, PanelBorder = PanelBorder, Surface = Surface, SurfaceBorder = SurfaceBorder,
        Footer = Footer, TextPrimary = TextPrimary, TextSecondary = TextSecondary,
        Accent = Accent, Hover = Hover, IconSurface = IconSurface,
    };
}
