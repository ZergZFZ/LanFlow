using System.ComponentModel;
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

    public Group? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (_selectedGroup == value)
            {
                return;
            }

            _selectedGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleItems));
            OnPropertyChanged(nameof(SelectedGroupName));
        }
    }

    public string SelectedGroupName => SelectedGroup?.Name ?? "没有分组";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

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
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public IEnumerable<LauncherItem> VisibleItems
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return OrderItems(SelectedGroup);
            }

            return Config.Groups
                .SelectMany(OrderItems)
                .Where(item =>
                    item.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                    item.Path.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    private static IEnumerable<LauncherItem> OrderItems(Group? group)
    {
        if (group is null)
        {
            return [];
        }

        return group.SortMode == "frequency"
            ? group.Items.OrderByDescending(item => item.UseCount)
            : group.Items;
    }

    public void RefreshGroups()
    {
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(SelectedGroupName));
        OnPropertyChanged(nameof(VisibleItems));
    }

    public void RefreshVisibleItems() => OnPropertyChanged(nameof(VisibleItems));

    public Settings Settings => Config.Settings;

    public bool ShowShortcutBadge
    {
        get => Config.Settings.ShowShortcutBadge;
        set
        {
            if (Config.Settings.ShowShortcutBadge == value) return;
            Config.Settings.ShowShortcutBadge = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool ShowFullItemName
    {
        get => Config.Settings.ShowFullItemName;
        set
        {
            if (Config.Settings.ShowFullItemName == value) return;
            Config.Settings.ShowFullItemName = value;
            OnPropertyChanged();
            Save();
        }
    }

    public string GroupLayout
    {
        get => Config.Settings.GroupLayout;
        set
        {
            var normalizedValue = value == "top" ? "top" : "left";
            if (Config.Settings.GroupLayout == normalizedValue) return;
            Config.Settings.GroupLayout = normalizedValue;
            OnPropertyChanged();
            Save();
        }
    }

    public void UpdateAppearance(string theme, double opacity, bool showShortcutBadge, bool showFullItemName, string groupLayout)
    {
        Config.Settings.Theme = theme == "dark" ? "dark" : "light";
        Config.Settings.Opacity = Math.Clamp(opacity, 0.55, 1.0);
        Config.Settings.ShowShortcutBadge = showShortcutBadge;
        Config.Settings.ShowFullItemName = showFullItemName;
        Config.Settings.GroupLayout = groupLayout == "top" ? "top" : "left";

        OnPropertyChanged(nameof(ShowShortcutBadge));
        OnPropertyChanged(nameof(ShowFullItemName));
        OnPropertyChanged(nameof(GroupLayout));
        Save();
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
