using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Windows.Media;

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

    [JsonPropertyName("useCount")]
    public long UseCount { get; set; }

    [JsonIgnore]
    public string DisplayName => Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? Name[..^4] : Name;

    [JsonIgnore]
    public ImageSource? IconImage { get; set; }
}

public sealed class Settings
{
    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "Alt+Space";

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "dark";

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1.0;

    [JsonPropertyName("showShortcutBadge")]
    public bool ShowShortcutBadge { get; set; }

    [JsonPropertyName("showFullItemName")]
    public bool ShowFullItemName { get; set; }

    [JsonPropertyName("groupLayout")]
    public string GroupLayout { get; set; } = "left";
}
