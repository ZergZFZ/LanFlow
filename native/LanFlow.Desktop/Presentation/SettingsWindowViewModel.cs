using System.ComponentModel;
using System.Runtime.CompilerServices;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Presentation;

public sealed class SettingsWindowViewModel : INotifyPropertyChanged
{
    private SettingsCategory _selectedCategory;

    public SettingsWindowViewModel(SettingsPreviewSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Categories = CreateCategories();
        _selectedCategory = Categories[0];
    }

    public SettingsPreviewSession Session { get; }

    public IReadOnlyList<SettingsCategory> Categories { get; }

    public SettingsCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Equals(_selectedCategory, value)) return;
            _selectedCategory = value;
            OnPropertyChanged();
        }
    }

    public Settings Working => Session.Working;

    public bool HasChanges => Session.HasChanges;

    public bool IsLeftNavigationWidthEnabled =>
        string.Equals(Working.GroupLayout, SettingsOptionValues.GroupLeft, StringComparison.Ordinal);

    public double CurrentOpacity =>
        string.Equals(Working.TransparencyMode, SettingsOptionValues.TransparencyWholeWindow, StringComparison.Ordinal)
            ? Working.WholeWindowOpacity
            : Working.LayeredOpacity;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(Action<Settings> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        Session.Update(mutation);
        NotifySettingsStateChanged();
    }

    public Settings Apply() => Working.Clone();

    public Settings Cancel()
    {
        var restored = Session.Cancel();
        NotifySettingsStateChanged();
        return restored;
    }

    public void ResetCurrentOpacity()
    {
        Update(settings =>
        {
            if (string.Equals(
                    settings.TransparencyMode,
                    SettingsOptionValues.TransparencyWholeWindow,
                    StringComparison.Ordinal))
            {
                settings.WholeWindowOpacity = 0.85;
            }
            else
            {
                settings.LayeredOpacity = 0.85;
            }

            settings.Opacity = 0.85;
        });
    }

    private void NotifySettingsStateChanged()
    {
        OnPropertyChanged(nameof(Working));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(IsLeftNavigationWidthEnabled));
        OnPropertyChanged(nameof(CurrentOpacity));
    }

    private static IReadOnlyList<SettingsCategory> CreateCategories() =>
    [
        new(
            "appearance",
            "外观与主题",
            "主题预设与自定义颜色",
            ["theme", "themeProfile", "themeColors", "customThemes"]),
        new(
            "layout",
            "布局与项目",
            "排列方式、尺寸与信息密度",
            [
                "layoutMode", "iconSize", "cardWidth", "cardHeight", "cardSize", "textSize",
                "itemSpacing", "rowSpacing", "contentPadding", "showShortcutBadge",
                "showFullItemName", "showItemTitle",
            ]),
        new(
            "groups",
            "分组标签",
            "位置、切换方式与标签尺寸",
            ["groupLayout", "groupSwitchMode", "groupLabelSize", "groupLabelFontSize", "groupNavigationWidth"]),
        new(
            "transparency",
            "透明度与材质",
            "分层或整窗透明效果",
            ["opacity", "transparencyMode", "layeredOpacity", "wholeWindowOpacity"]),
        new(
            "interaction",
            "交互与动画",
            "打开方式与动画偏好",
            ["animationMode", "openItemsOnSingleClick"]),
        new(
            "startup",
            "启动与快捷键",
            "全局热键与登录启动",
            ["hotkey", "startWithWindows"]),
        new(
            "performance",
            "性能与缓存",
            "图标缓存与性能说明",
            []),
        new(
            "about",
            "关于",
            "版本、更新与开源信息",
            []),
    ];

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
