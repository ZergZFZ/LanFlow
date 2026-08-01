using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LanFlow.Core.Collections;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IConfigStore _configStore;
    private readonly RangeObservableCollection<LauncherItem> _visibleItems = [];
    private Group? _selectedGroup;
    private string _searchText = string.Empty;
    private string _statusText = "就绪";

    public MainViewModel(IConfigStore configStore)
    {
        _configStore = configStore;
        Config = _configStore.Load();
        VisibleItems = new ReadOnlyObservableCollection<LauncherItem>(_visibleItems);
        SelectedGroup = RestoreLastGroup();
        RefreshVisibleItems();
    }

    public AppConfig Config { get; private set; }
    public IEnumerable<Group> Groups => Config.Groups;
    public Settings Settings => Config.Settings;

    public Group? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (_selectedGroup == value) return;
            _selectedGroup = value;
            Config.Settings.LastGroupId = value?.Id;
            OnPropertyChanged();
            RefreshVisibleItems();
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
            RefreshVisibleItems();
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
        $"LanFlow · 主题={(Settings.Theme == "light" ? "light" : "dark")} · " +
        $"布局={LayoutLabel} · 分组切换={GroupSwitchLabel} · 分组数={Config.Groups.Count} · 当前分组={SelectedGroupName}";

    private string LayoutLabel => Settings.LayoutMode switch
    {
        SettingsOptionValues.CardLayout => "卡片",
        _ => "网格",
    };

    private string GroupSwitchLabel => Settings.GroupSwitchMode switch
    {
        SettingsOptionValues.GroupSwitchHover => "悬停",
        _ => "点击",
    };

    public ReadOnlyObservableCollection<LauncherItem> VisibleItems { get; }

    private static IEnumerable<LauncherItem> OrderItems(Group? group) => group is null
        ? []
        : group.SortMode == "frequency" ? group.Items.OrderByDescending(item => item.UseCount) : group.Items;

    public void RefreshGroups()
    {
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(SelectedGroupName));
        RefreshVisibleItems();
        OnPropertyChanged(nameof(InfoText));
    }

    public void RefreshVisibleItems()
    {
        // 先清除旧的分组标注，再填充搜索结果；避免非搜索态残留上次搜索的分组名。
        foreach (var item in Config.Groups.SelectMany(group => group.Items))
        {
            item.SearchGroupName = null;
        }

        IEnumerable<LauncherItem> query = string.IsNullOrWhiteSpace(SearchText)
            ? OrderItems(SelectedGroup)
            : SearchWorkspace();
        _visibleItems.ReplaceRange(query.ToArray());
    }

    private List<LauncherItem> SearchWorkspace()
    {
        var matches = WorkspaceSearch.Search(Config, SearchText);
        foreach (var match in matches)
        {
            match.Item.SearchGroupName = match.Group.Name;
        }

        return matches.Select(match => match.Item).ToList();
    }

    public void ApplyAppearance(Settings source, bool persist)
    {
        var settings = Config.Settings;
        settings.Theme = source.Theme == "light" ? "light" : "dark";
        settings.ThemeProfile = source.ThemeProfile;
        settings.ThemeColors = source.ThemeColors;
        settings.CustomThemes = source.CustomThemes;
        settings.LayoutMode = source.LayoutMode == SettingsOptionValues.CardLayout
            ? SettingsOptionValues.CardLayout
            : SettingsOptionValues.GridLayout;
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
        settings.GroupLayout = source.GroupLayout == SettingsOptionValues.GroupTop
            ? SettingsOptionValues.GroupTop
            : SettingsOptionValues.GroupLeft;
        settings.GroupSwitchMode = source.GroupSwitchMode == SettingsOptionValues.GroupSwitchHover
            ? SettingsOptionValues.GroupSwitchHover
            : SettingsOptionValues.GroupSwitchClick;
        settings.GroupLabelSize = Math.Clamp(source.GroupLabelSize, 28, 52);
        settings.GroupLabelFontSize = Math.Clamp(source.GroupLabelFontSize, 11, 18);
        settings.GroupNavigationWidth = Math.Clamp(source.GroupNavigationWidth, 96, 280);
        settings.TransparencyMode = source.TransparencyMode == SettingsOptionValues.TransparencyWholeWindow
            ? SettingsOptionValues.TransparencyWholeWindow
            : SettingsOptionValues.TransparencyLayered;
        settings.LayeredOpacity = Math.Clamp(source.LayeredOpacity, 0.40, 1.00);
        settings.WholeWindowOpacity = Math.Clamp(source.WholeWindowOpacity, 0.40, 1.00);
        settings.Opacity = settings.TransparencyMode == SettingsOptionValues.TransparencyWholeWindow
            ? settings.WholeWindowOpacity
            : settings.LayeredOpacity;
        settings.AnimationMode = source.AnimationMode is SettingsOptionValues.AnimationOn or SettingsOptionValues.AnimationOff
            ? source.AnimationMode
            : SettingsOptionValues.AnimationSystem;
        settings.Hotkey = source.Hotkey;
        settings.StartWithWindows = source.StartWithWindows;
        settings.OpenItemsOnSingleClick = source.OpenItemsOnSingleClick;
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

    public void SaveAndApply(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var selectedGroupId = SelectedGroup?.Id;

        // 提交边界只负责持久化和内存交换。UI 通知由调用方在预览窗口关闭后执行，
        // 避免保存成功后的事件处理异常被误判为“保存失败”并允许重复提交。
        _configStore.Save(config);
        Config = config;
        _selectedGroup = selectedGroupId is null
            ? FirstNonEmptyGroup()
            : Config.Groups.FirstOrDefault(group => string.Equals(group.Id, selectedGroupId, StringComparison.Ordinal))
              ?? FirstNonEmptyGroup();
        Config.Settings.LastGroupId = _selectedGroup?.Id;
    }

    // 启动与配置替换时默认选中第一个非空分组，避免打开就停在空分组上
    // （空分组会显示“此分组还没有启动项目”，容易被误认为界面空白）。
    private Group? FirstNonEmptyGroup() =>
        Config.Groups.FirstOrDefault(group => group.Items.Count > 0)
        ?? Config.Groups.FirstOrDefault();

    // 优先恢复上次停留的分组；找不到或该分组为空时回退到第一个非空分组。
    private Group? RestoreLastGroup()
    {
        var lastId = Config.Settings.LastGroupId;
        if (!string.IsNullOrWhiteSpace(lastId))
        {
            var last = Config.Groups.FirstOrDefault(group =>
                string.Equals(group.Id, lastId, StringComparison.Ordinal));
            if (last is not null && last.Items.Count > 0)
            {
                return last;
            }
        }

        return FirstNonEmptyGroup();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
