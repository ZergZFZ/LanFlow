using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ConfigStore _configStore;
    private Group? _selectedGroup;
    private string _searchText = string.Empty;
    private string _statusText = "就绪";

    public MainViewModel(ConfigStore configStore)
    {
        _configStore = configStore;
        Config = _configStore.Load();
        SelectedGroup = Config.Groups.FirstOrDefault();
    }

    public AppConfig Config { get; }
    public IEnumerable<Group> Groups => Config.Groups;
    public Settings Settings => Config.Settings;

    public Group? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (_selectedGroup == value) return;
            _selectedGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleItems));
            OnPropertyChanged(nameof(SelectedGroupName));
            OnPropertyChanged(nameof(InfoText));
        }
    }

    public string SelectedGroupName => SelectedGroup?.Name ?? "没有分组";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleItems));
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    // 状态栏/调试信息，Desktop 与 Linux 共用，具体文案由各自 UI 决定如何展示。
    public string InfoText =>
        $"LanFlow · 主题={(Settings.Theme == "light" ? "light" : "dark")} · 分组数={Config.Groups.Count} · 当前分组={SelectedGroupName}";

    public IEnumerable<LauncherItem> VisibleItems => string.IsNullOrWhiteSpace(SearchText)
        ? OrderItems(SelectedGroup)
        : Config.Groups.SelectMany(OrderItems).Where(item =>
            item.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
            item.Path.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
            item.Command.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));

    private static IEnumerable<LauncherItem> OrderItems(Group? group) => group is null
        ? []
        : group.SortMode == "frequency" ? group.Items.OrderByDescending(item => item.UseCount) : group.Items;

    public void RefreshGroups()
    {
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(SelectedGroupName));
        OnPropertyChanged(nameof(VisibleItems));
        OnPropertyChanged(nameof(InfoText));
    }

    public void RefreshVisibleItems() => OnPropertyChanged(nameof(VisibleItems));

    public void ApplyAppearance(Settings source, bool persist)
    {
        var settings = Config.Settings;
        settings.Theme = source.Theme == "light" ? "light" : "dark";
        settings.ThemeProfile = source.ThemeProfile;
        settings.ThemeColors = source.ThemeColors;
        settings.CustomThemes = source.CustomThemes;
        settings.Opacity = Math.Clamp(source.Opacity, 0.55, 1.0);
        settings.LayoutMode = source.LayoutMode == "card" ? "card" : "tile";
        settings.IconSize = Math.Clamp(source.IconSize, 24, 72);
        settings.CardWidth = Math.Clamp(source.CardWidth, 48, 320);
        settings.CardHeight = Math.Clamp(source.CardHeight, 48, 240);
        settings.CardSize = Math.Clamp(source.CardSize, 76, 160);
        settings.TextSize = Math.Clamp(source.TextSize, 10, 18);
        settings.ItemSpacing = Math.Clamp(source.ItemSpacing, 0, 64);
        settings.RowSpacing = Math.Clamp(source.RowSpacing, 0, 80);
        settings.ContentPadding = Math.Clamp(source.ContentPadding, 6, 40);
        settings.ShowShortcutBadge = source.ShowShortcutBadge;
        settings.ShowFullItemName = source.ShowFullItemName;
        settings.ShowItemTitle = source.ShowItemTitle;
        settings.GroupLayout = source.GroupLayout == "top" ? "top" : "left";
        settings.Hotkey = source.Hotkey;
        settings.StartWithWindows = source.StartWithWindows;
        settings.OpenItemsOnSingleClick = source.OpenItemsOnSingleClick;
        settings.HideOnDeactivate = source.HideOnDeactivate;
        settings.GroupSwitchMode = source.GroupSwitchMode == "hover" ? "hover" : "click";
        settings.GroupHoverDelayMs = Math.Clamp(source.GroupHoverDelayMs, 0, 1000);
        settings.AnimationMode = source.AnimationMode == "off" ? "off" : "on";
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(VisibleItems));
        OnPropertyChanged(nameof(InfoText));
        if (persist) Save();
    }

    public void Save()
    {
        _configStore.Save(Config);
        StatusText = "已保存";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
