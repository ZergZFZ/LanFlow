using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace LanFlow.Desktop.Models;

public sealed class AppConfig
{
    // 配置结构版本：缺失字段按版本 0（旧格式）处理；当前版本为 1。
    // 版本 1 仅新增顶层字段，不重排、不删改用户已有数据。
    public const int CurrentVersion = 1;

    [JsonPropertyName("configVersion")]
    public int ConfigVersion { get; set; }

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

public sealed class LauncherItem : INotifyPropertyChanged
{
    private object? _iconImage;
    private int _iconRequestVersion;
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

    // 仅搜索态展示用：搜索结果显示所属分组名；非搜索态为 null。不参与序列化与合并。
    [JsonIgnore]
    public string? SearchGroupName { get; set; }

    // 平台无关的图标承载：Desktop 塞 System.Windows.Media.ImageSource，Linux 塞 Avalonia.Media.IImage。
    [JsonIgnore]
    public object? IconImage
    {
        get => _iconImage;
        set
        {
            if (ReferenceEquals(_iconImage, value)) return;
            _iconImage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconImage)));
        }
    }

    [JsonIgnore]
    public int IconRequestVersion
    {
        get => _iconRequestVersion;
        set => _iconRequestVersion = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class Settings
{
    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "Alt+Space";

    [JsonPropertyName("screenshotHotkey")]
    public string ScreenshotHotkey { get; set; } = "Ctrl+Shift+A";

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
    public string GroupLayout { get; set; } = SettingsOptionValues.GroupLeft;

    [JsonPropertyName("groupSwitchMode")]
    public string GroupSwitchMode { get; set; } = SettingsOptionValues.GroupSwitchClick;

    [JsonPropertyName("groupLabelSize")]
    public double GroupLabelSize { get; set; } = 36;

    [JsonPropertyName("groupLabelFontSize")]
    public double GroupLabelFontSize { get; set; } = 13;

    [JsonPropertyName("groupNavigationWidth")]
    public double GroupNavigationWidth { get; set; } = 132;

    [JsonPropertyName("transparencyMode")]
    public string? TransparencyMode { get; set; }

    [JsonPropertyName("layeredOpacity")]
    public double LayeredOpacity { get; set; } = 0.85;

    [JsonPropertyName("wholeWindowOpacity")]
    public double WholeWindowOpacity { get; set; } = 0.85;

    [JsonPropertyName("animationMode")]
    public string AnimationMode { get; set; } = SettingsOptionValues.AnimationSystem;

    // 分组切换时的内容过渡动画（透明度/位移）。默认关闭，避免切换时图标跳动。
    [JsonPropertyName("groupTransitionAnimation")]
    public bool GroupTransitionAnimation { get; set; }

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    [JsonPropertyName("openItemsOnSingleClick")]
    public bool OpenItemsOnSingleClick { get; set; } = true;

    [JsonPropertyName("groupHoverDelayMs")]
    public int GroupHoverDelayMs { get; set; } = SettingsOptionValues.DefaultGroupHoverDelayMs;

    // 上次停留的分组；缺失或找不到时回退到第一个非空分组。非用户主动编辑字段。
    [JsonPropertyName("lastGroupId")]
    public string? LastGroupId { get; set; }

    public Settings Clone() => new()
    {
        Hotkey = Hotkey,
        ScreenshotHotkey = ScreenshotHotkey,
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
        GroupSwitchMode = GroupSwitchMode,
        GroupLabelSize = GroupLabelSize,
        GroupLabelFontSize = GroupLabelFontSize,
        GroupNavigationWidth = GroupNavigationWidth,
        TransparencyMode = TransparencyMode,
        LayeredOpacity = LayeredOpacity,
        WholeWindowOpacity = WholeWindowOpacity,
        AnimationMode = AnimationMode,
        GroupTransitionAnimation = GroupTransitionAnimation,
        StartWithWindows = StartWithWindows,
        OpenItemsOnSingleClick = OpenItemsOnSingleClick,
        GroupHoverDelayMs = GroupHoverDelayMs,
        LastGroupId = LastGroupId,
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
