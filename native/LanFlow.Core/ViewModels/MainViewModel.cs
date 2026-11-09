using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IConfigStore _configStore;
    private Group? _selectedGroup;
    private string _searchText = string.Empty;
    private string _statusText = "就绪";

    public MainViewModel(IConfigStore configStore)
    {
        _configStore = configStore;
        Config = _configStore.Load();
        // B5-3：首启即落盘，确保 config.json 首次启动即生成（含版本号），
        // 避免"启动后配置文件不存在"导致取证/换位置验证缺位。
        _configStore.Save(Config);
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
        // 复制与归一化分离（对齐 Windows 主线 v2.0.1）：CopyAll 反射覆盖所有可写字段，
        // 新增设置字段自动纳入；Normalize 只负责夹紧、枚举合法性、旧热键迁移与派生值。
        // 二者都不再依赖人工字段清单，从结构上消除「设置保存后不落盘」。
        var settings = Config.Settings;
        SettingsPipeline.CopyAll(source, settings, skipRuntimeOnly: true);
        SettingsPipeline.Normalize(settings, _configStore.DefaultHotkey);
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
