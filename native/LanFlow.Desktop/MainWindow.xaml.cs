using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LanFlow.Desktop.Views;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;
using LanFlow.Desktop.ViewModels;

namespace LanFlow.Desktop;

public partial class MainWindow : System.Windows.Window
{
    private readonly MainViewModel _viewModel;
    private readonly LauncherService _launcherService = new();
    private readonly HotkeyService _hotkeyService = new();
    private readonly ShellIconService _shellIconService = new();
    private readonly ShortcutService _shortcutService = new();
    private bool _isEditMode;

    public static readonly DependencyProperty IsEditModeProperty =
        DependencyProperty.Register(
            nameof(IsEditMode),
            typeof(bool),
            typeof(MainWindow),
            new PropertyMetadata(false));

    public bool IsEditMode
    {
        get => (bool)GetValue(IsEditModeProperty);
        set => SetValue(IsEditModeProperty, value);
    }

    private int _openContextMenus;
    private bool _isContextMenuActivationPending;
    private Point _dragStartPoint;
    private LauncherItem? _draggedItem;
    private Group? _dragSourceGroup;
    private int _dragSourceIndex = -1;
    private Popup? _dragGhost;
    private LauncherItem? _previewTargetItem;
    private int _previewInsertIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new ConfigStore());
        DataContext = _viewModel;
        ApplySettings();
        LoadIcons();
        RefreshGroupTabs();
        RefreshEmptyState();

        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
            {
                hwndSource.AddHook(WndProc);
            }

            if (!_hotkeyService.Register(this, ShowFromHotkey))
            {
                _viewModel.StatusText = "全局快捷键 Alt+Space 注册失败";
            }
        };
        Closed += (_, _) => _hotkeyService.Dispose();
    }

    private void ShowFromHotkey()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmNcHitTest = 0x0084;
        if (msg != wmNcHitTest || WindowState == WindowState.Maximized)
        {
            return IntPtr.Zero;
        }

        var screenX = lParam.ToInt32() & 0xFFFF;
        var screenY = (lParam.ToInt32() >> 16) & 0xFFFF;
        var point = PointFromScreen(new Point(screenX, screenY));

        const double edge = 6;
        var width = ActualWidth;
        var height = ActualHeight;
        var left = point.X <= edge;
        var right = point.X >= width - edge;
        var top = point.Y <= edge;
        var bottom = point.Y >= height - edge;

        int result;
        if (left && top)
        {
            result = 13; // HTTOPLEFT
        }
        else if (right && top)
        {
            result = 14; // HTTOPRIGHT
        }
        else if (left && bottom)
        {
            result = 16; // HTBOTTOMLEFT
        }
        else if (right && bottom)
        {
            result = 17; // HTBOTTOMRIGHT
        }
        else if (left)
        {
            result = 10; // HTLEFT
        }
        else if (right)
        {
            result = 11; // HTRIGHT
        }
        else if (top)
        {
            result = 12; // HTTOP
        }
        else if (bottom)
        {
            result = 15; // HTBOTTOM
        }
        else
        {
            return IntPtr.Zero;
        }

        handled = true;
        return (IntPtr)result;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (!IsVisible || _isEditMode || IsExiting())
        {
            return;
        }

        // 延迟到后台优先级再决定是否隐藏：右键弹出 ContextMenu 时窗口会先失焦，
        // 但 ContextMenu 的 Opening 计数可能在失焦事件之后才被派发；
        // 若立即隐藏就会出现“右键闪退”。延迟到 Background 优先级可确保计数已稳定。
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (IsVisible && !_isEditMode && !IsExiting() && _openContextMenus == 0 && !_isContextMenuActivationPending)
            {
                Hide();
            }
        });
    }

    private static bool IsExiting() => ((App)System.Windows.Application.Current).IsExiting;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (IsExiting())
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        Hide();
        _viewModel.StatusText = "已隐藏到系统托盘";
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void HideWindow_Click(object sender, RoutedEventArgs e) => Hide();

    private void LauncherLayout_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            FindAncestor<TextBox>(source) is not null ||
            FindAncestor<Button>(source) is not null ||
            FindAncestor<ListBoxItem>(source) is not null ||
            FindAncestor<ScrollBar>(source) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标捕获中断时不影响启动器交互。
        }
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        _viewModel.SearchText = string.Empty;
        Focus();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_viewModel.Settings) { Owner = this };
        if (settingsWindow.ShowDialog() != true)
        {
            return;
        }

        _viewModel.UpdateAppearance(
            settingsWindow.Theme,
            settingsWindow.PanelOpacity,
            settingsWindow.ShowShortcutBadge,
            settingsWindow.ShowFullItemName,
            settingsWindow.GroupLayout);
        ApplySettings();
        RefreshGroupTabs();
    }

    private void ApplySettings()
    {
        var isLightTheme = _viewModel.Settings.Theme == "light";
        SetBrush("PanelBrush", isLightTheme ? "#F6F7FB" : "#171B28");
        SetBrush("PanelBorderBrush", isLightTheme ? "#CCD2E0" : "#343B50");
        SetBrush("SurfaceBrush", isLightTheme ? "#FFFFFF" : "#22283A");
        SetBrush("SurfaceBorderBrush", isLightTheme ? "#D7DCE8" : "#38425B");
        SetBrush("FooterBrush", isLightTheme ? "#EEF1F7" : "#1D2231");
        SetBrush("TextPrimaryBrush", isLightTheme ? "#1E2533" : "#F5F7FC");
        SetBrush("TextSecondaryBrush", isLightTheme ? "#59657A" : "#ADB5C7");
        SetBrush("AccentBrush", isLightTheme ? "#DCE7FA" : "#35405E");
        SetBrush("SelectedTileBrush", isLightTheme ? "#6600B7C3" : "#35405E");
        SetBrush("HoverBrush", isLightTheme ? "#E9EDF5" : "#2B3247");
        SetBrush("IconSurfaceBrush", isLightTheme ? "#E2E6ED" : "#2A3040");
        Opacity = Math.Clamp(_viewModel.Settings.Opacity, 0.55, 1.0);

        var groupsAtTop = _viewModel.GroupLayout == "top";
        GroupColumn.Width = groupsAtTop ? new GridLength(0) : new GridLength(132);
        GroupSeparatorColumn.Width = groupsAtTop ? new GridLength(0) : new GridLength(14);
        GroupRow.Height = groupsAtTop ? new GridLength(42) : new GridLength(0);

        Grid.SetRow(GroupTabsHost, groupsAtTop ? 1 : 2);
        Grid.SetColumn(GroupTabsHost, 0);
        Grid.SetColumnSpan(GroupTabsHost, groupsAtTop ? 3 : 1);
        GroupTabsHost.Margin = groupsAtTop ? new Thickness(0, 8, 0, 0) : new Thickness(0, 12, 0, 0);
        GroupTabsHost.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        GroupTabsHost.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        GroupTabs.Orientation = groupsAtTop ? Orientation.Horizontal : Orientation.Vertical;

        GroupSeparator.Visibility = Visibility.Collapsed;
        Grid.SetRow(ItemListHost, 2);
        Grid.SetColumn(ItemListHost, groupsAtTop ? 0 : 2);
        Grid.SetColumnSpan(ItemListHost, groupsAtTop ? 3 : 1);
        ItemListHost.Margin = groupsAtTop ? new Thickness(0, 4, 0, 0) : new Thickness(0, 4, 0, 0);
    }

    private void SetBrush(string key, string color)
    {
        Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void ToggleEditMode_Click(object sender, RoutedEventArgs e)
    {
        _isEditMode = !_isEditMode;
        IsEditMode = _isEditMode;
        EditHint.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;
        _viewModel.StatusText = _isEditMode ? "编辑模式：右键管理项目和分组" : "就绪";
    }

    private void RefreshGroupTabs()
    {
        GroupTabs.Children.Clear();
        foreach (var group in _viewModel.Config.Groups)
        {
            var button = new Button
            {
                Content = group.Name,
                Style = (Style)FindResource("TabButton"),
                Tag = group,
                FontWeight = group == _viewModel.SelectedGroup ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = group == _viewModel.SelectedGroup
                    ? (System.Windows.Media.Brush)FindResource("TextPrimaryBrush")
                    : (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                Background = group == _viewModel.SelectedGroup
                    ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                    : System.Windows.Media.Brushes.Transparent,
            };
            button.Click += GroupTab_Click;
            button.MouseEnter += GroupTab_MouseEnter;
            button.AllowDrop = true;
            button.DragOver += GroupTab_DragOver;
            button.Drop += GroupTab_Drop;
            button.ContextMenu = BuildGroupContextMenu(group);
            button.PreviewMouseRightButtonDown += Item_PreviewMouseRightButtonDown;
            button.ContextMenuOpening += (_, _) =>
            {
                _isContextMenuActivationPending = false;
                _openContextMenus++;
            };
            GroupTabs.Children.Add(button);
        }
    }

    private void GroupTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Group group })
        {
            SelectGroup(group);
        }
    }

    private void GroupTab_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Button { Tag: Group group } && !_isEditMode && group != _viewModel.SelectedGroup)
        {
            SelectGroup(group);
        }
    }

    private void GroupTab_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = _isEditMode && e.Data.GetDataPresent(typeof(LauncherItem))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupTab_Drop(object sender, DragEventArgs e)
    {
        if (!_isEditMode || sender is not Button { Tag: Group targetGroup } ||
            e.Data.GetData(typeof(LauncherItem)) is not LauncherItem item || _dragSourceGroup is null)
        {
            return;
        }

        MoveItem(item, _dragSourceGroup, targetGroup, targetGroup.Items.Count);
        _draggedItem = null;
        _dragSourceGroup = null;
    }

    private void SelectGroup(Group group)
    {
        _viewModel.SelectedGroup = group;
        _viewModel.SearchText = string.Empty;
        RefreshGroupTabs();
        RefreshEmptyState();
    }

    private void RefreshEmptyState()
    {
        EmptyPanel.Visibility = _viewModel.VisibleItems.Any() ? Visibility.Collapsed : Visibility.Visible;
    }

    private void LoadIcons()
    {
        foreach (var item in _viewModel.Config.Groups.SelectMany(group => group.Items))
        {
            item.IconImage = _shellIconService.GetIcon(item.Path);
        }
    }

    private void ItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LaunchSelectedItem();

    private void ItemList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isEditMode)
        {
            return;
        }

        var item = GetParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (GetParent<Button>(e.OriginalSource as DependencyObject)?.Tag as string == "DeleteItem")
        {
            return;
        }

        if (item?.DataContext is LauncherItem launcherItem && _viewModel.SelectedGroup is { } group)
        {
            ItemList.SelectedItem = launcherItem;
            _dragStartPoint = e.GetPosition(ItemList);
            _draggedItem = launcherItem;
            _dragSourceGroup = group;
            _dragSourceIndex = group.Items.IndexOf(launcherItem);
        }
    }

    private static T? GetParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T parent)
            {
                return parent;
            }

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void Item_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isContextMenuActivationPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _isContextMenuActivationPending = false);
    }

    private void ItemContextMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        _isContextMenuActivationPending = false;
        _openContextMenus++;
        if (sender is FrameworkElement { DataContext: LauncherItem item })
        {
            ItemList.SelectedItem = item;
        }
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void LaunchSelectedItem_Click(object sender, RoutedEventArgs e) => LaunchSelectedItem();

    private void LaunchSelectedItem()
    {
        if (ItemList.SelectedItem is not LauncherItem item)
        {
            return;
        }

        try
        {
            _launcherService.Open(item.Path);
            item.UseCount++;
            _viewModel.RefreshVisibleItems();
            _viewModel.Save();
            _viewModel.StatusText = $"已启动：{item.Name}";
            if (!_isEditMode)
            {
                Hide();
            }
        }
        catch (Exception exception)
        {
            _viewModel.StatusText = "无法启动项目";
            MessageBox.Show(exception.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        NewGroupNameBox.Text = GetUniqueGroupName("新建分组");
        NewGroupOverlay.Visibility = Visibility.Visible;
        NewGroupNameBox.Focus();
        NewGroupNameBox.SelectAll();
    }

    private void NewGroupNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmNewGroup();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseNewGroupOverlay();
            e.Handled = true;
        }
    }

    private void ConfirmNewGroup_Click(object sender, RoutedEventArgs e) => ConfirmNewGroup();

    private void CancelNewGroup_Click(object sender, RoutedEventArgs e) => CloseNewGroupOverlay();

    private void ConfirmNewGroup()
    {
        var name = NewGroupNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _viewModel.StatusText = "请输入分组名称";
            NewGroupNameBox.Focus();
            return;
        }

        if (_viewModel.Config.Groups.Any(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            _viewModel.StatusText = "已存在同名分组";
            NewGroupNameBox.Focus();
            NewGroupNameBox.SelectAll();
            return;
        }

        var group = new Group { Name = name };
        _viewModel.Config.Groups.Add(group);
        _viewModel.SelectedGroup = group;
        _viewModel.Save();
        RefreshGroupTabs();
        RefreshEmptyState();
        CloseNewGroupOverlay();
    }

    private void CloseNewGroupOverlay()
    {
        NewGroupOverlay.Visibility = Visibility.Collapsed;
        Focus();
    }

    private void RenameSelectedGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGroup is { } group)
        {
            RenameGroup(group);
        }
    }

    private void RenameGroup(Group group)
    {
        var dialog = new Views.EditGroupWindow(group.Name, "重命名分组") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        group.Name = dialog.GroupName;
        _viewModel.RefreshGroups();
        _viewModel.Save();
        RefreshGroupTabs();
    }

    private void DeleteSelectedGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGroup is { } group)
        {
            DeleteGroup(group);
        }
    }

    private void DeleteGroup(Group group)
    {
        if (MessageBox.Show($"确定删除分组“{group.Name}”及其中的所有项目吗？", "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var index = _viewModel.Config.Groups.IndexOf(group);
        _viewModel.Config.Groups.Remove(group);
        _viewModel.SelectedGroup = _viewModel.Config.Groups.ElementAtOrDefault(Math.Min(index, _viewModel.Config.Groups.Count - 1));
        _viewModel.RefreshGroups();
        _viewModel.Save();
        RefreshGroupTabs();
        RefreshEmptyState();
    }

    private ContextMenu BuildGroupContextMenu(Group group)
    {
        var contextMenu = new ContextMenu
        {
            Style = (Style)FindResource("LauncherContextMenu"),
        };

        contextMenu.Closed += LauncherContextMenu_Closed;

        var menuItemStyle = (Style)FindResource("LauncherMenuItem");
        var renameItem = new MenuItem { Header = "修改名称", Tag = group, Style = menuItemStyle };
        renameItem.Click += RenameGroupMenu_Click;
        var deleteItem = new MenuItem { Header = "删除分组", Tag = group, Style = menuItemStyle };
        deleteItem.Click += DeleteGroupMenu_Click;

        var sortMenu = new MenuItem { Header = "排序", Style = menuItemStyle };
        var customSortItem = new MenuItem
        {
            Header = "自定义排序",
            Tag = group,
            Style = menuItemStyle,
            IsCheckable = true,
            IsChecked = group.SortMode != "frequency",
        };
        customSortItem.Click += SetGroupCustomSort_Click;

        var frequencySortItem = new MenuItem
        {
            Header = "使用频率排序",
            Tag = group,
            Style = menuItemStyle,
            IsCheckable = true,
            IsChecked = group.SortMode == "frequency",
        };
        frequencySortItem.Click += SetGroupFrequencySort_Click;
        sortMenu.Items.Add(customSortItem);
        sortMenu.Items.Add(frequencySortItem);

        contextMenu.Items.Add(sortMenu);
        contextMenu.Items.Add(renameItem);
        contextMenu.Items.Add(deleteItem);
        return contextMenu;
    }

    private void SetGroupCustomSort_Click(object sender, RoutedEventArgs e)
    {
        SetGroupSortMode(sender, "custom");
    }

    private void SetGroupFrequencySort_Click(object sender, RoutedEventArgs e)
    {
        SetGroupSortMode(sender, "frequency");
    }

    private void SetGroupSortMode(object sender, string sortMode)
    {
        if (sender is not MenuItem { Tag: Group group } || group.SortMode == sortMode)
        {
            return;
        }

        group.SortMode = sortMode;
        _viewModel.RefreshVisibleItems();
        _viewModel.Save();
        _viewModel.StatusText = sortMode == "frequency" ? "已按使用频率排序" : "已切换为自定义排序";
    }

    private void RenameGroupMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: Group group })
        {
            RenameGroup(group);
        }
    }

    private void DeleteGroupMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: Group group })
        {
            DeleteGroup(group);
        }
    }

    private void LauncherContextMenu_Closed(object? sender, RoutedEventArgs e)
    {
        _isContextMenuActivationPending = false;
        _openContextMenus = Math.Max(0, _openContextMenus - 1);
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGroup is null)
        {
            MessageBox.Show("请先创建或选择一个分组。", "无法添加项目", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.EditItemWindow(string.Empty, string.Empty, "添加项目") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (_viewModel.SelectedGroup.Items.Any(item => string.Equals(item.Path, dialog.ItemPath, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("当前分组已经包含该项目。", "无法添加项目", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var item = new LauncherItem { Name = dialog.ItemName, Path = dialog.ItemPath };
        item.IconImage = _shellIconService.GetIcon(item.Path);
        _viewModel.SelectedGroup.Items.Add(item);
        _viewModel.RefreshVisibleItems();
        _viewModel.Save();
        RefreshEmptyState();
    }

    private void EditSelectedItem_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGroup is null || ItemList.SelectedItem is not LauncherItem item)
        {
            return;
        }

        var dialog = new Views.EditItemWindow(item.Name, item.Path, "编辑项目") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (_viewModel.SelectedGroup.Items.Any(existing => existing != item && string.Equals(existing.Path, dialog.ItemPath, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("当前分组已经包含该项目。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        item.Name = dialog.ItemName;
        item.Path = dialog.ItemPath;
        item.IconImage = _shellIconService.GetIcon(item.Path);
        _viewModel.RefreshVisibleItems();
        _viewModel.Save();
    }

    private void RemoveSelectedItem_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGroup is null || ItemList.SelectedItem is not LauncherItem item)
        {
            return;
        }

        if (MessageBox.Show($"确定从“{_viewModel.SelectedGroup.Name}”移除“{item.Name}”吗？", "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _viewModel.SelectedGroup.Items.Remove(item);
        _viewModel.RefreshVisibleItems();
        _viewModel.Save();
        RefreshEmptyState();
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LauncherItem item } || _viewModel.SelectedGroup is not { } group)
        {
            return;
        }

        group.Items.Remove(item);
        _viewModel.RefreshVisibleItems();
        _viewModel.Save();
        RefreshEmptyState();
        _viewModel.StatusText = $"已删除：{item.Name}";
    }

    private void ItemDeleteButton_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void ItemList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (_isEditMode && e.Data.GetData(typeof(LauncherItem)) is LauncherItem item)
        {
            UpdateDragGhost(e.GetPosition(this));
            PreviewItemOrder(item, GetParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as LauncherItem);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void ItemList_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel.SelectedGroup is null)
        {
            return;
        }

        if (_isEditMode && e.Data.GetData(typeof(LauncherItem)) is LauncherItem item && _dragSourceGroup is not null)
        {
            if (ReferenceEquals(_dragSourceGroup, _viewModel.SelectedGroup) && _previewInsertIndex >= 0)
            {
                _previewTargetItem = null;
                _previewInsertIndex = -1;
                _viewModel.RefreshVisibleItems();
                _viewModel.Save();
                _viewModel.StatusText = "项目顺序已更新";
            }
            else
            {
                RestorePreviewOrder();
                MoveItem(item, _dragSourceGroup, _viewModel.SelectedGroup, GetInsertIndex(GetParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as LauncherItem));
            }

            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        var addedCount = AddPathsToSelectedGroup(paths);
        _viewModel.StatusText = addedCount == 0 ? "没有可添加的新项目" : $"已添加 {addedCount} 个项目";
        if (addedCount > 0)
        {
            _viewModel.RefreshVisibleItems();
            _viewModel.Save();
            RefreshEmptyState();
        }
    }

    private void ItemList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isEditMode || e.LeftButton != MouseButtonState.Pressed || _draggedItem is null || _dragSourceGroup is null)
        {
            return;
        }

        var position = e.GetPosition(ItemList);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        ShowDragGhost(_draggedItem, e.GetPosition(this));
        try
        {
            DragDrop.DoDragDrop(ItemList, new DataObject(typeof(LauncherItem), _draggedItem), DragDropEffects.Move);
        }
        finally
        {
            RestorePreviewOrder();
            CloseDragGhost();
            _draggedItem = null;
            _dragSourceGroup = null;
            _dragSourceIndex = -1;
        }
    }

    private void ItemList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggedItem = null;
        _dragSourceGroup = null;
        _dragSourceIndex = -1;
    }

    private void PreviewItemOrder(LauncherItem item, LauncherItem? targetItem)
    {
        if (_dragSourceGroup is null || !ReferenceEquals(_dragSourceGroup, _viewModel.SelectedGroup) || targetItem is null || ReferenceEquals(item, targetItem) || ReferenceEquals(_previewTargetItem, targetItem))
        {
            return;
        }

        var currentIndex = _viewModel.SelectedGroup!.Items.IndexOf(item);
        var targetIndex = _viewModel.SelectedGroup.Items.IndexOf(targetItem);
        if (currentIndex < 0 || targetIndex < 0)
        {
            return;
        }

        _viewModel.SelectedGroup.Items.Move(currentIndex, targetIndex);
        _previewTargetItem = targetItem;
        _previewInsertIndex = targetIndex;
    }

    private void RestorePreviewOrder()
    {
        if (_draggedItem is not null && _dragSourceGroup is not null && ReferenceEquals(_dragSourceGroup, _viewModel.SelectedGroup))
        {
            var currentIndex = _viewModel.SelectedGroup.Items.IndexOf(_draggedItem);
            var sourceIndex = _dragSourceGroup.Items.IndexOf(_draggedItem);
            if (currentIndex >= 0 && sourceIndex >= 0 && currentIndex != sourceIndex)
            {
                _viewModel.SelectedGroup.Items.Move(currentIndex, sourceIndex);
            }
        }

        _previewTargetItem = null;
        _previewInsertIndex = -1;
    }

    private int GetInsertIndex(LauncherItem? targetItem)
    {
        var index = targetItem is null ? -1 : _viewModel.SelectedGroup!.Items.IndexOf(targetItem);
        return index >= 0 ? index : _viewModel.SelectedGroup!.Items.Count;
    }

    private void ShowDragGhost(LauncherItem item, Point position)
    {
        _dragGhost = new Popup
        {
            AllowsTransparency = true,
            IsHitTestVisible = false,
            PlacementTarget = this,
            Placement = PlacementMode.Relative,
            Child = new Border
            {
                Width = 82,
                Padding = new Thickness(8, 6, 8, 7),
                Background = new SolidColorBrush(Color.FromRgb(51, 58, 72)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(100, 198, 226)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Opacity = 0.78,
                Child = new StackPanel
                {
                    Children =
                    {
                        new Image { Source = item.IconImage ?? _shellIconService.GetIcon(item.Path), Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = item.Name, MaxWidth = 66, Margin = new Thickness(0, 5, 0, 0), FontSize = 11, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }
                    }
                }
            },
            IsOpen = true
        };
        UpdateDragGhost(position);
    }

    private void UpdateDragGhost(Point position)
    {
        if (_dragGhost is not null)
        {
            _dragGhost.HorizontalOffset = position.X + 14;
            _dragGhost.VerticalOffset = position.Y + 14;
        }
    }

    private void CloseDragGhost()
    {
        if (_dragGhost is not null)
        {
            _dragGhost.IsOpen = false;
            _dragGhost = null;
        }
    }

    private void MoveItem(LauncherItem item, Group sourceGroup, Group targetGroup, int targetIndex)
    {
        var sourceIndex = sourceGroup.Items.IndexOf(item);
        if (sourceIndex < 0)
        {
            return;
        }

        if (sourceGroup == targetGroup && sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        sourceGroup.Items.Remove(item);
        targetIndex = Math.Clamp(targetIndex, 0, targetGroup.Items.Count);
        targetGroup.Items.Insert(targetIndex, item);
        SelectGroup(targetGroup);
        _viewModel.RefreshVisibleItems();
        _viewModel.Save();
        _viewModel.StatusText = sourceGroup == targetGroup ? "项目顺序已更新" : $"已移至“{targetGroup.Name}”";
    }

    private int AddPathsToSelectedGroup(IEnumerable<string> paths)
    {
        if (_viewModel.SelectedGroup is null)
        {
            return 0;
        }

        var addedCount = 0;
        foreach (var sourcePath in paths.Where(path => File.Exists(path) || Directory.Exists(path)))
        {
            var path = _shortcutService.ResolveTargetPath(sourcePath);
            if (_viewModel.SelectedGroup.Items.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var item = new LauncherItem { Name = Path.GetFileNameWithoutExtension(path), Path = path };
            item.IconImage = _shellIconService.GetIcon(path);
            _viewModel.SelectedGroup.Items.Insert(0, item);
            addedCount++;
        }

        return addedCount;
    }

    private string GetUniqueGroupName(string initialName)
    {
        if (_viewModel.Config.Groups.All(group => !string.Equals(group.Name, initialName, StringComparison.CurrentCultureIgnoreCase)))
        {
            return initialName;
        }

        var suffix = 2;
        while (_viewModel.Config.Groups.Any(group => string.Equals(group.Name, $"{initialName} {suffix}", StringComparison.CurrentCultureIgnoreCase)))
        {
            suffix++;
        }

        return $"{initialName} {suffix}";
    }
}
