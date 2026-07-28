using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;
using LanFlow.Desktop.ViewModels;
using LanFlow.Linux;

namespace LanFlow.Desktop;

public sealed partial class MainWindow : Window
{
    private MainViewModel _viewModel = null!;
    private readonly LauncherService _launcher = new();
    private readonly ShellIconService _shellIcon = new();
    private HotkeyService? _hotkey;
    private bool _editMode;
    private bool _exiting;
    private LauncherItem? _dragItem;
    private Point _dragStart;
    private bool _isDragging;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new ConfigStore());
        DataContext = _viewModel;

        // 关键：让窗口以 32 位 ARGB 视觉创建，运行时调整 Opacity 才不会触发 X11 窗口重建而卡死
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        App.ApplyThemeColors(_viewModel.Settings);
        ApplyMetrics(_viewModel.Settings);
        Opacity = _viewModel.Settings.Opacity;
        BuildGroupTabs();
        ReloadItems();

        Closing += OnClosing;
    }

    public void EnableHotkey()
    {
        _hotkey = new HotkeyService();
        var registered = _hotkey.Register(this, () => Dispatcher.UIThread.Post(ToggleVisibility), _viewModel.Settings.Hotkey);
        if (!registered)
        {
            _viewModel.StatusText = string.IsNullOrEmpty(_hotkey.LastError)
                ? "全局热键不可用"
                : _hotkey.LastError;
        }
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
        }
    }

    public void Quit()
    {
        _exiting = true;
        _hotkey?.Dispose();
        Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _viewModel.SearchText = SearchBox.Text ?? string.Empty;
        ReloadItems();
    }

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new Views.SettingsWindow(_viewModel);
        settingsWindow.OnApplied = RefreshAfterSettings;
        await settingsWindow.ShowDialog(this);
    }

    private void RefreshAfterSettings()
    {
        App.ApplyThemeColors(_viewModel.Settings);
        ApplyMetrics(_viewModel.Settings);
        Opacity = _viewModel.Settings.Opacity;
        BuildGroupTabs();
        ReloadItems();
        _hotkey?.TryRegister(_viewModel.Settings.Hotkey);
    }

    private void ApplyMetrics(Settings settings)
    {
        var resources = Resources;
        resources["IconSize"] = settings.IconSize;
        resources["TextSize"] = settings.TextSize;
        resources["CardSize"] = settings.CardSize;
        resources["ShowTitle"] = settings.ShowItemTitle;
        resources["ShowBadge"] = settings.ShowShortcutBadge;
        resources["EditMode"] = _editMode;
        resources["ContentPadding"] = new Thickness(settings.ContentPadding);
        resources["ItemMargin"] = new Thickness(
            settings.ItemSpacing / 2.0, settings.RowSpacing / 2.0, settings.ItemSpacing / 2.0, settings.RowSpacing / 2.0);

        ItemsControl.ItemTemplate = (DataTemplate)(settings.LayoutMode == "card"
            ? resources["CardTemplate"]!
            : resources["TileTemplate"]!);

        if (settings.GroupLayout == "top")
        {
            DockPanel.SetDock(GroupsHost, Dock.Top);
            GroupTabs.Orientation = Orientation.Horizontal;
        }
        else
        {
            DockPanel.SetDock(GroupsHost, Dock.Left);
            GroupTabs.Orientation = Orientation.Vertical;
        }
    }

    private void BuildGroupTabs()
    {
        GroupTabs.Children.Clear();
        foreach (var group in _viewModel.Groups)
        {
            var isSelected = group == _viewModel.SelectedGroup;
            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = (SolidColorBrush)(isSelected ? Resources["AccentBrush"]! : Resources["SurfaceBrush"]!),
                BorderBrush = (SolidColorBrush)Resources["SurfaceBorderBrush"]!,
                BorderThickness = new Thickness(1),
            };
            border.Child = new TextBlock
            {
                Text = group.Name,
                Foreground = (SolidColorBrush)Resources["TextPrimaryBrush"]!,
                FontSize = 13,
            };
            border.PointerPressed += (_, _) => SelectGroup(group);

            DragDrop.SetAllowDrop(border, true);
            border.AddHandler(DragDrop.DragOverEvent, (s, ev) =>
            {
                if (ev is DragEventArgs dragOverArgs)
                {
                    OnGroupTabDragOver(s, dragOverArgs);
                }
            });
            border.AddHandler(DragDrop.DropEvent, (s, ev) =>
            {
                if (ev is DragEventArgs dragEventArgs)
                {
                    OnGroupTabDrop(group, dragEventArgs);
                }
            });

            GroupTabs.Children.Add(border);
        }
    }

    private void SelectGroup(Group group)
    {
        _viewModel.SelectedGroup = group;
        BuildGroupTabs();
        ReloadItems();
    }

    private void ReloadItems()
    {
        LoadIcons();
        ItemsControl.ItemsSource = _viewModel.VisibleItems;
    }

    private void LoadIcons()
    {
        foreach (var item in _viewModel.VisibleItems)
        {
            if (item.IconImage is null)
            {
                item.IconImage = _shellIcon.GetIcon(item);
            }
        }
    }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_editMode || sender is not Control control)
        {
            return;
        }

        if (control.DataContext is not LauncherItem item)
        {
            return;
        }

        _dragItem = item;
        _dragStart = e.GetPosition(this);
        _isDragging = false;
    }

    private async void OnItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_editMode || _dragItem is null || _isDragging || sender is not Control control)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _dragStart.X) < 4 && Math.Abs(point.Y - _dragStart.Y) < 4)
        {
            return;
        }

        _isDragging = true;
        var data = new DataObject();
        data.Set("item", _dragItem);
        data.Set("group", _viewModel.SelectedGroup!);
        try
        {
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
        catch
        {
            // ignore drag failures
        }
    }

    private void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_editMode && _dragItem is not null && !_isDragging)
        {
            OpenEditor(_dragItem);
        }

        _dragItem = null;
        _isDragging = false;
    }

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (_editMode || !_viewModel.Settings.OpenItemsOnSingleClick)
        {
            return;
        }

        if (sender is Control control && control.DataContext is LauncherItem item)
        {
            LaunchItem(item);
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not LauncherItem item)
        {
            return;
        }

        if (_editMode)
        {
            OpenEditor(item);
            return;
        }

        if (!_viewModel.Settings.OpenItemsOnSingleClick)
        {
            LaunchItem(item);
        }
    }

    // 拖拽悬停时给出有效反馈：否则外部文件管理器拖入会被视为「禁止」而取消放置
    private void OnItemsDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains("item") || e.Data.Contains(DataFormats.FileNames)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnItemsDrop(object? sender, DragEventArgs e)
    {
        // 外部文件（来自文件管理器）拖入
        if (e.Data.Contains(DataFormats.FileNames))
        {
            var paths = e.Data.Get(DataFormats.FileNames) as IEnumerable<string>;
            if (paths is not null)
            {
                DropFiles(paths, _viewModel.SelectedGroup);
            }

            e.DragEffects = DragDropEffects.Copy;
            return;
        }

        if (!e.Data.Contains("item"))
        {
            return;
        }

        var item = e.Data.Get("item") as LauncherItem;
        var sourceGroup = e.Data.Get("group") as Group;
        if (item is null)
        {
            return;
        }

        LauncherItem? target = e.Source is Control control && control.DataContext is LauncherItem li ? li : null;
        var destGroup = _viewModel.SelectedGroup;
        if (destGroup is null || item == target)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        if (sourceGroup is not null && sourceGroup.Items.Contains(item))
        {
            sourceGroup.Items.Remove(item);
        }

        var index = target is null ? destGroup.Items.Count : destGroup.Items.IndexOf(target);
        if (index < 0)
        {
            index = destGroup.Items.Count;
        }

        destGroup.Items.Insert(index, item);
        _viewModel.Save();
        ReloadItems();
        e.DragEffects = DragDropEffects.Move;
    }

    private void OnGroupTabDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains("item") || e.Data.Contains(DataFormats.FileNames)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnGroupTabDrop(Group targetGroup, DragEventArgs e)
    {
        // 外部文件拖到分组标签上：加入该分组
        if (e.Data.Contains(DataFormats.FileNames))
        {
            var paths = e.Data.Get(DataFormats.FileNames) as IEnumerable<string>;
            if (paths is not null)
            {
                DropFiles(paths, targetGroup);
            }

            e.DragEffects = DragDropEffects.Copy;
            return;
        }

        if (!e.Data.Contains("item"))
        {
            return;
        }

        var item = e.Data.Get("item") as LauncherItem;
        var sourceGroup = e.Data.Get("group") as Group;
        if (item is null)
        {
            return;
        }

        if (sourceGroup is not null && sourceGroup.Items.Contains(item))
        {
            sourceGroup.Items.Remove(item);
        }

        if (!targetGroup.Items.Contains(item))
        {
            targetGroup.Items.Add(item);
        }

        _viewModel.SelectedGroup = targetGroup;
        _viewModel.Save();
        BuildGroupTabs();
        ReloadItems();
        e.DragEffects = DragDropEffects.Move;
    }

    // 把从系统拖入的文件路径转换为启动项，加入指定分组；若未指定分组且无分组则自动创建
    private void DropFiles(IEnumerable<string> paths, Group? targetGroup)
    {
        var group = targetGroup ?? _viewModel.SelectedGroup;
        if (group is null)
        {
            group = new Group { Name = "我的应用" };
            _viewModel.Config.Groups.Add(group);
            _viewModel.SelectedGroup = group;
        }

        var added = 0;
        foreach (var raw in paths)
        {
            var path = NormalizePath(raw);
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            {
                continue;
            }

            group.Items.Add(CreateItemFromPath(path));
            added++;
        }

        if (added > 0)
        {
            _viewModel.Save();
            BuildGroupTabs();
            ReloadItems();
            _viewModel.StatusText = $"已添加 {added} 个项目到「{group.Name}」";
        }
        else
        {
            _viewModel.StatusText = "未能识别拖入的文件";
        }
    }

    private static string NormalizePath(string raw)
    {
        var path = raw.Trim();
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                path = new Uri(path).LocalPath;
            }
            catch
            {
                // ignore，保留原始值
            }
        }

        return path;
    }

    private static LauncherItem CreateItemFromPath(string path)
    {
        var item = new LauncherItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "app",
            IsEnabled = true,
            Path = path,
        };

        if (path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
        {
            var (name, _, icon) = ShellIconService.ParseDesktop(path);
            item.Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name;
            item.Icon = icon;
        }
        else
        {
            var name = Path.GetFileNameWithoutExtension(path);
            item.Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name;
        }

        return item;
    }

    private void OnDeleteItem(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not LauncherItem item)
        {
            return;
        }

        var group = _viewModel.SelectedGroup;
        if (group is null)
        {
            return;
        }

        group.Items.Remove(item);
        _viewModel.Save();
        ReloadItems();
        _viewModel.StatusText = "已删除项目";
    }

    private void LaunchItem(LauncherItem item)
    {
        if (!item.IsEnabled)
        {
            _viewModel.StatusText = "该项目已禁用";
            return;
        }

        try
        {
            if (item.IsCommand && !string.IsNullOrWhiteSpace(item.Command))
            {
                _launcher.LaunchCommand(item.Command);
            }
            else
            {
                _launcher.Open(item.Path);
            }

            item.UseCount++;
            _viewModel.Save();
            _viewModel.StatusText = "正在启动：" + item.DisplayName;
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "启动失败：" + ex.Message;
        }
    }

    private void OnToggleEdit(object? sender, RoutedEventArgs e)
    {
        _editMode = !_editMode;
        Resources["EditMode"] = _editMode;
        EditToggleButton.Content = _editMode ? "完成" : "编辑";
        BuildGroupTabs();
    }

    private void OnAddGroup(object? sender, RoutedEventArgs e)
    {
        var group = new Group { Name = "新分组" };
        _viewModel.Config.Groups.Add(group);
        _viewModel.SelectedGroup = group;
        try
        {
            _viewModel.Save();
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "保存失败：" + ex.Message;
        }

        BuildGroupTabs();
        ReloadItems();
        _viewModel.StatusText = "已新建分组：" + group.Name;
    }

    private async void OnRenameGroup(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGroup is null)
        {
            return;
        }

        var dialog = new Views.EditGroupWindow { GroupName = _viewModel.SelectedGroup.Name };
        await dialog.ShowDialog(this);
        if (dialog.Confirmed)
        {
            _viewModel.SelectedGroup.Name = dialog.GroupName;
            _viewModel.Save();
            _viewModel.RefreshGroups();
            BuildGroupTabs();
        }
    }

    private void OnDeleteGroup(object? sender, RoutedEventArgs e)
    {
        var group = _viewModel.SelectedGroup;
        if (group is null || _viewModel.Config.Groups.Count <= 1)
        {
            return;
        }

        _viewModel.Config.Groups.Remove(group);
        _viewModel.SelectedGroup = _viewModel.Config.Groups.FirstOrDefault();
        _viewModel.Save();
        BuildGroupTabs();
        ReloadItems();
    }

    private async void OnAddItem(object? sender, RoutedEventArgs e)
    {
        var group = _viewModel.SelectedGroup;
        if (group is null)
        {
            group = new Group { Name = "我的应用" };
            _viewModel.Config.Groups.Add(group);
            _viewModel.SelectedGroup = group;
            BuildGroupTabs();
        }

        var item = new LauncherItem { Name = "新项目", Kind = "app" };
        var dialog = new Views.EditItemWindow();
        dialog.InitializeDialog(item);
        await dialog.ShowDialog(this);
        if (dialog.Confirmed && (!string.IsNullOrWhiteSpace(item.Path) || item.IsCommand))
        {
            group.Items.Add(item);
            try
            {
                _viewModel.Save();
            }
            catch (Exception ex)
            {
                _viewModel.StatusText = "保存失败：" + ex.Message;
            }

            ReloadItems();
            _viewModel.StatusText = "已添加项目：" + item.DisplayName;
        }
    }

    private async void OpenEditor(LauncherItem item)
    {
        var dialog = new Views.EditItemWindow();
        dialog.InitializeDialog(item);
        await dialog.ShowDialog(this);
        if (dialog.Confirmed)
        {
            item.IconImage = null;
            _viewModel.Save();
            ReloadItems();
        }
    }
}
