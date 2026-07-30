using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading.Tasks;
using LanFlow.Desktop.Controls;
using LanFlow.Desktop.Diagnostics;
using LanFlow.Desktop.Presentation;
using LanFlow.Desktop.Views;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;
using LanFlow.Desktop.ViewModels;

namespace LanFlow.Desktop;

public partial class MainWindow : System.Windows.Window
{
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int SWP_FRAMECHANGED = 0x0020;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOZORDER = 0x0004;
    private const double DragAutoScrollEdge = 32;
    private const double DragAutoScrollStep = 16;

    private readonly MainViewModel _viewModel;
    private readonly LauncherService _launcherService = new();
    private readonly HotkeyService _hotkeyService = new();
    private readonly StartupService _startupService = new();
    private readonly IIconService _iconService = new ShellIconService();
    private readonly ViewportIconCoordinator _iconCoordinator;
    private readonly GroupSwitchCoordinator _groupSwitchCoordinator;
    private readonly UiPerformanceTrace _uiPerformanceTrace = new();
    private readonly ThemeResourceUpdater _themeResourceUpdater = new();
    private readonly WindowAppearanceController _windowAppearanceController = new();
    private readonly ShortcutService _shortcutService = new();
    private readonly ImportManifestService _importManifestService;
    private Settings? _settingsBeforePreview;
    private bool _isEditMode;
    private bool _isModalOperationActive;
    private long _latestGroupSwitchGeneration;
    private readonly Dictionary<(string GroupId, string LayoutMode), ViewStateSnapshot> _viewStateSnapshots = [];
    private VirtualizingWrapPanel? _wrapPanel;
    private ScrollViewer? _itemScrollViewer;
    private bool _isClosed;
    private string _activeLayoutMode = "tile";
    private string? _lastSelectedItemId;

    private sealed record ViewStateSnapshot(
        string? SelectedItemId,
        string? FocusedItemId,
        double VerticalOffset);

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
    private bool _dragStartedWhileFiltering;
    private Popup? _dragGhost;
    private LauncherItem? _previewTargetItem;
    private int _previewInsertIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new ConfigStore("Alt+Space"));
        _importManifestService = new ImportManifestService(_shortcutService);
        _iconCoordinator = new ViewportIconCoordinator(_iconService);
        _groupSwitchCoordinator = new GroupSwitchCoordinator(
            new DispatcherTimerScheduler(Dispatcher),
            TimeSpan.FromMilliseconds(200));
        _groupSwitchCoordinator.SelectedGroupId = _viewModel.SelectedGroup?.Id;
        _groupSwitchCoordinator.SwitchRequested += GroupSwitchCoordinator_SwitchRequested;
        DataContext = _viewModel;
        ((INotifyCollectionChanged)_viewModel.VisibleItems).CollectionChanged += VisibleItems_CollectionChanged;
        _activeLayoutMode = NormalizeLayoutMode(_viewModel.Settings.LayoutMode);
        ItemList.Loaded += (_, _) =>
        {
            AttachVirtualizingPanel();
            _ = LoadVisibleIconsAsync();
        };
        ItemList.SizeChanged += ItemList_SizeChanged;
        ApplySettings();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _ = LoadVisibleIconsAsync();
            }
            else
            {
                _iconCoordinator.CancelPending();
            }
        };
        RefreshEmptyState();

        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
            {
                hwndSource.AddHook(WndProc);
            }

            AddDwmShadow();

            if (!_hotkeyService.Register(this, ShowFromHotkey, _viewModel.Settings.Hotkey))
            {
                _viewModel.StatusText = $"全局快捷键 {_viewModel.Settings.Hotkey} 注册失败";
            }
        };
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        _isClosed = true;
        ((INotifyCollectionChanged)_viewModel.VisibleItems).CollectionChanged -= VisibleItems_CollectionChanged;
        ItemList.SizeChanged -= ItemList_SizeChanged;
        DetachVirtualizingPanel();
        _groupSwitchCoordinator.SwitchRequested -= GroupSwitchCoordinator_SwitchRequested;
        _groupSwitchCoordinator.Dispose();
        _iconCoordinator.Dispose();
        _hotkeyService.Dispose();
        await _iconService.DisposeAsync();
    }

    private void ShowFromHotkey()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    private void AddDwmShadow()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        // 恢复 WS_THICKFRAME，让 DWM 认为这是一个可调整大小的窗口，从而绘制原生阴影
        var style = GetWindowLong(hwnd, GWL_STYLE);
        SetWindowLong(hwnd, GWL_STYLE, style | WS_THICKFRAME);

        // 让 DWM 将 frame 扩展到客户区边缘，触发原生阴影绘制
        var margins = new MARGINS
        {
            cxLeftWidth = 1,
            cxRightWidth = 1,
            cyTopHeight = 1,
            cyBottomHeight = 1
        };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        // 通知窗口 frame 已变更
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmNcHitTest = 0x0084;
        const int wmNcCalcSize = 0x0083;

        if (msg == wmNcCalcSize)
        {
            // 阻止 WS_THICKFRAME 带来的可调整行为，但保留 DWM 原生阴影
            handled = true;
            return IntPtr.Zero;
        }

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

        if (!IsVisible || _isEditMode || _isModalOperationActive || IsExiting())
        {
            return;
        }

        // 延迟到后台优先级再决定是否隐藏：右键弹出 ContextMenu 时窗口会先失焦，
        // 但 ContextMenu 的 Opening 计数可能在失焦事件之后才被派发；
        // 若立即隐藏就会出现“右键闪退”。延迟到 Background 优先级可确保计数已稳定。
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (IsVisible && !_isEditMode && !_isModalOperationActive && !IsExiting() && _openContextMenus == 0 && !_isContextMenuActivationPending)
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

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
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

    private void ImportManifest_Click(object sender, RoutedEventArgs e)
    {
        var previousStatus = _viewModel.StatusText;
        string? finalStatus = null;
        _isModalOperationActive = true;

        try
        {
            var fileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 LanFlow 导入清单",
                Filter = "JSON 清单 (*.json)|*.json|所有文件 (*.*)|*.*",
                DefaultExt = ".json",
                CheckFileExists = true,
                Multiselect = false,
            };

            _viewModel.StatusText = "请选择 import-manifest.json";
            if (fileDialog.ShowDialog(this) != true)
            {
                finalStatus = previousStatus;
                return;
            }

            ImportPreview preview;
            try
            {
                preview = _importManifestService.LoadPreview(fileDialog.FileName, _viewModel.Config);
            }
            catch (ImportManifestException ex)
            {
                MessageBox.Show(this, ex.Message, "导入清单无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                finalStatus = "导入清单校验失败";
                return;
            }

            var previewWindow = new ImportPreviewWindow(preview, selection =>
            {
                var result = _importManifestService.BuildMerge(_viewModel.Config, selection);
                _viewModel.SaveAndApply(result.Config);
                return result;
            })
            {
                Owner = this,
            };

            if (previewWindow.ShowDialog() == true && previewWindow.Result is { } result)
            {
                var successStatus = $"已导入 {result.ImportedItemCount} 个项目，创建 {result.ImportedGroupCount} 个分组，跳过 {result.SkippedItemCount} 个项目";
                finalStatus = successStatus;
                try
                {
                    // 提交已完成；后续仅同步界面，不再把刷新异常误报为保存失败或允许重复提交。
                    _viewModel.RefreshGroups();
                    _viewModel.RefreshVisibleItems();
                    RefreshEmptyState();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"导入已保存，但界面刷新失败：{ex.Message}\n请重启 LanFlow 查看导入结果。", "导入已保存", MessageBoxButton.OK, MessageBoxImage.Warning);
                    finalStatus = $"{successStatus}；界面刷新失败，请重启 LanFlow";
                }
            }
            else
            {
                finalStatus = "已取消导入";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导入失败：{ex.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
            finalStatus = "导入失败，配置未更改";
        }
        finally
        {
            _isModalOperationActive = false;
            _viewModel.StatusText = finalStatus ?? previousStatus;
            Activate();
        }
    }
    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _settingsBeforePreview = CloneSettings(_viewModel.Settings);
        var wasEditMode = _isEditMode;
        SetEditMode(true, "设置中：可同时查看和管理启动项");
        var settingsWindow = new SettingsWindow(_viewModel.Settings) { Owner = this };
        settingsWindow.PreviewChanged += settings => { _viewModel.ApplyAppearance(settings, persist: false); ApplySettings(); };

        if (settingsWindow.ShowDialog() == true)
        {
            var result = settingsWindow.Result;
            if (!_hotkeyService.TryRegister(result.Hotkey))
            {
                result.Hotkey = _settingsBeforePreview.Hotkey;
                _viewModel.StatusText = "快捷键被其他程序占用，已保留原组合键";
            }
            result.StartWithWindows = _startupService.SetEnabled(result.StartWithWindows) && _startupService.IsEnabled();
            if (result.StartWithWindows != settingsWindow.Result.StartWithWindows) _viewModel.StatusText = "开机启动设置失败，请检查当前用户注册表权限";
            _viewModel.ApplyAppearance(result, persist: true);
        }
        else if (_settingsBeforePreview is not null)
        {
            _startupService.SetEnabled(_settingsBeforePreview.StartWithWindows);
            _viewModel.ApplyAppearance(_settingsBeforePreview, persist: false);
        }

        ApplySettings();
        SetEditMode(settingsWindow.DialogResult == true ? false : wasEditMode, settingsWindow.DialogResult == true ? "设置已保存" : null);
        _settingsBeforePreview = null;
    }

    private void ApplySettings()
    {
        var settings = _viewModel.Settings;
        _themeResourceUpdater.Apply(Resources, settings.ThemeColors);
        _windowAppearanceController.Apply(this, SurfaceRoot, ContentRoot, settings);
        LauncherLayout.Margin = new Thickness(settings.ContentPadding, Math.Max(8, settings.ContentPadding - 4), settings.ContentPadding, Math.Max(8, settings.ContentPadding - 4));

        string requestedLayoutMode = NormalizeLayoutMode(settings.LayoutMode);
        bool layoutModeChanged = !string.Equals(_activeLayoutMode, requestedLayoutMode, StringComparison.Ordinal);
        if (layoutModeChanged)
        {
            SaveCurrentViewState(_activeLayoutMode);
        }

        bool listMode = requestedLayoutMode == "list";
        bool cardMode = requestedLayoutMode == "card";
        ItemList.ItemContainerStyle = (Style)FindResource(
            listMode ? "LauncherList" : cardMode ? "LauncherCard" : "LauncherTile");
        ItemList.ItemTemplate = (DataTemplate)FindResource(
            listMode || cardMode ? "CardItemTemplate" : "TileItemTemplate");

        var requestedItemsPanel = (ItemsPanelTemplate)FindResource(
            listMode ? "VirtualizingListItemsPanel" : "VirtualizingWrapItemsPanel");
        if (!ReferenceEquals(ItemList.ItemsPanel, requestedItemsPanel))
        {
            DetachVirtualizingPanel();
            ItemList.ItemsPanel = requestedItemsPanel;
        }

        _activeLayoutMode = requestedLayoutMode;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            AttachVirtualizingPanel();
            RestoreViewState(reuseOffset: !layoutModeChanged);
            _ = LoadVisibleIconsAsync();
        });

        var groupsAtTop = settings.GroupLayout == SettingsOptionValues.GroupTop;
        GroupColumn.Width = groupsAtTop ? new GridLength(0) : new GridLength(settings.GroupNavigationWidth);
        GroupSeparatorColumn.Width = groupsAtTop ? new GridLength(0) : new GridLength(14);
        GroupRow.Height = groupsAtTop ? new GridLength(settings.GroupLabelSize + 8) : new GridLength(0);
        Grid.SetRow(GroupNavigation, groupsAtTop ? 1 : 2);
        Grid.SetColumn(GroupNavigation, 0);
        Grid.SetColumnSpan(GroupNavigation, groupsAtTop ? 3 : 1);
        GroupNavigation.Width = groupsAtTop ? double.NaN : settings.GroupNavigationWidth;
        GroupNavigation.Margin = groupsAtTop ? new Thickness(0, 8, 0, 0) : new Thickness(0, 12, 0, 0);
        GroupSeparator.Visibility = Visibility.Collapsed;
        Grid.SetRow(ItemListHost, 2);
        Grid.SetColumn(ItemListHost, groupsAtTop ? 0 : 2);
        Grid.SetColumnSpan(ItemListHost, groupsAtTop ? 3 : 1);
        ItemListHost.Margin = new Thickness(0, 4, 0, 0);
    }

    private static string NormalizeLayoutMode(string? layoutMode) =>
        layoutMode is "card" or "list" ? layoutMode : "tile";

    private void VisibleItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PreserveCurrentSnapshotAfterCollectionChange();
        _ = LoadVisibleIconsAsync();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => RestoreViewState(reuseOffset: true));
    }

    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemList.SelectedItem is LauncherItem item)
        {
            _lastSelectedItemId = item.Id;
        }

        SaveCurrentViewState(_activeLayoutMode);
    }

    private void ItemList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LaunchSelectedItem();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            if (ItemList.SelectedIndex < 0 && ItemList.Items.Count > 0)
            {
                ItemList.SelectedIndex = 0;
            }

            FocusSelectedContainer();
            e.Handled = true;
            return;
        }

        if (_activeLayoutMode == "list" ||
            e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down) ||
            ItemList.Items.Count == 0)
        {
            return;
        }

        AttachVirtualizingPanel();
        int columns = Math.Max(1, _wrapPanel?.RealizedRange.Columns ?? 1);
        int currentIndex = ItemList.SelectedIndex >= 0 ? ItemList.SelectedIndex : 0;
        var direction = e.Key switch
        {
            Key.Left => NavigationDirection.Left,
            Key.Right => NavigationDirection.Right,
            Key.Up => NavigationDirection.Up,
            _ => NavigationDirection.Down
        };
        var layout = new VirtualizingWrapLayout(
            _viewModel.Settings.CardWidth,
            _viewModel.Settings.CardHeight,
            _viewModel.Settings.ItemSpacing,
            _viewModel.Settings.RowSpacing,
            bufferRows: 1);
        int nextIndex = layout.MoveIndex(currentIndex, direction, ItemList.Items.Count, columns);
        ItemList.SelectedIndex = nextIndex;
        ItemList.ScrollIntoView(ItemList.Items[nextIndex]);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FocusSelectedContainer);
        e.Handled = true;
    }

    private void SaveCurrentViewState(string layoutMode)
    {
        if (_viewModel.SelectedGroup is not { } group)
        {
            return;
        }

        string? selectedId = (ItemList.SelectedItem as LauncherItem)?.Id ?? _lastSelectedItemId;
        string? focusedId = (FindAncestor<ListBoxItem>(Keyboard.FocusedElement as DependencyObject)?.DataContext as LauncherItem)?.Id;
        _viewStateSnapshots[(group.Id, layoutMode)] = new ViewStateSnapshot(
            selectedId,
            focusedId,
            GetCurrentVerticalOffset());
    }

    private void PreserveCurrentSnapshotAfterCollectionChange()
    {
        if (_viewModel.SelectedGroup is not { } group)
        {
            return;
        }

        var key = (group.Id, _activeLayoutMode);
        _viewStateSnapshots.TryGetValue(key, out ViewStateSnapshot? previous);
        string? selectedId = (ItemList.SelectedItem as LauncherItem)?.Id
            ?? previous?.SelectedItemId
            ?? _lastSelectedItemId;
        string? focusedId = (FindAncestor<ListBoxItem>(Keyboard.FocusedElement as DependencyObject)?.DataContext as LauncherItem)?.Id
            ?? previous?.FocusedItemId;
        _viewStateSnapshots[key] = new ViewStateSnapshot(
            selectedId,
            focusedId,
            GetCurrentVerticalOffset());
    }

    private void RestoreViewState(bool reuseOffset)
    {
        if (_viewModel.SelectedGroup is not { } group)
        {
            return;
        }

        _viewStateSnapshots.TryGetValue((group.Id, _activeLayoutMode), out ViewStateSnapshot? snapshot);
        string? selectedId = snapshot?.SelectedItemId ?? _lastSelectedItemId;
        LauncherItem? selected = selectedId is null
            ? null
            : _viewModel.VisibleItems.FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal));

        if (selected is not null)
        {
            ItemList.SelectedItem = selected;
        }

        if (reuseOffset && snapshot is not null)
        {
            SetCurrentVerticalOffset(snapshot.VerticalOffset);
        }
        else if (selected is not null)
        {
            ItemList.ScrollIntoView(selected);
        }

        string? focusId = snapshot?.FocusedItemId;
        if (focusId is null)
        {
            return;
        }

        LauncherItem? focused = _viewModel.VisibleItems.FirstOrDefault(
            item => string.Equals(item.Id, focusId, StringComparison.Ordinal));
        if (focused is not null && ItemList.ItemContainerGenerator.ContainerFromItem(focused) is ListBoxItem container)
        {
            container.Focus();
        }
    }

    private double GetCurrentVerticalOffset()
    {
        AttachVirtualizingPanel();
        if (_wrapPanel is not null)
        {
            return _wrapPanel.VerticalOffset;
        }

        return FindVisualChild<ScrollViewer>(ItemList)?.VerticalOffset ?? 0;
    }

    private void SetCurrentVerticalOffset(double offset)
    {
        AttachVirtualizingPanel();
        if (_wrapPanel is not null)
        {
            _wrapPanel.SetVerticalOffset(offset);
            return;
        }

        FindVisualChild<ScrollViewer>(ItemList)?.ScrollToVerticalOffset(offset);
    }

    private void FocusSelectedContainer()
    {
        if (ItemList.SelectedItem is not null &&
            ItemList.ItemContainerGenerator.ContainerFromItem(ItemList.SelectedItem) is ListBoxItem container)
        {
            container.Focus();
        }
    }

    private void AttachVirtualizingPanel()
    {
        var panel = FindVisualChild<VirtualizingWrapPanel>(ItemList);
        if (!ReferenceEquals(panel, _wrapPanel))
        {
            if (_wrapPanel is not null)
            {
                _wrapPanel.ViewportChanged -= VirtualizingPanel_ViewportChanged;
            }

            _wrapPanel = panel;
            if (_wrapPanel is not null)
            {
                _wrapPanel.ViewportChanged += VirtualizingPanel_ViewportChanged;
            }
        }

        var scrollViewer = FindVisualChild<ScrollViewer>(ItemList);
        if (!ReferenceEquals(scrollViewer, _itemScrollViewer))
        {
            if (_itemScrollViewer is not null)
            {
                _itemScrollViewer.ScrollChanged -= ItemScrollViewer_ScrollChanged;
            }

            _itemScrollViewer = scrollViewer;
            if (_itemScrollViewer is not null)
            {
                _itemScrollViewer.ScrollChanged += ItemScrollViewer_ScrollChanged;
            }
        }
    }

    private void DetachVirtualizingPanel()
    {
        if (_wrapPanel is not null)
        {
            _wrapPanel.ViewportChanged -= VirtualizingPanel_ViewportChanged;
            _wrapPanel = null;
        }

        if (_itemScrollViewer is not null)
        {
            _itemScrollViewer.ScrollChanged -= ItemScrollViewer_ScrollChanged;
            _itemScrollViewer = null;
        }
    }

    private void VirtualizingPanel_ViewportChanged(object? sender, ViewportRange range)
    {
        _ = LoadVisibleIconsAsync(range);
        if (_viewModel.SelectedGroup is { } group)
        {
            _uiPerformanceTrace.ContentStable(group.Id, _wrapPanel?.RealizedIndices.Count ?? 0);
        }
    }

    private void ItemScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_wrapPanel is null && e.VerticalChange != 0)
        {
            _ = LoadVisibleIconsAsync();
        }
    }

    private void ItemList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            AttachVirtualizingPanel();
            _ = LoadVisibleIconsAsync();
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static Settings CloneSettings(Settings value) => new()
    {
        Hotkey = value.Hotkey, Theme = value.Theme, ThemeProfile = value.ThemeProfile, Opacity = value.Opacity,
        LayoutMode = value.LayoutMode, IconSize = value.IconSize, CardWidth = value.CardWidth, CardHeight = value.CardHeight, TextSize = value.TextSize,
        ItemSpacing = value.ItemSpacing, RowSpacing = value.RowSpacing, ContentPadding = value.ContentPadding,
        ShowShortcutBadge = value.ShowShortcutBadge, ShowFullItemName = value.ShowFullItemName, ShowItemTitle = value.ShowItemTitle, GroupLayout = value.GroupLayout,
        StartWithWindows = value.StartWithWindows,
        ThemeColors = new ThemeColors { Panel = value.ThemeColors.Panel, PanelBorder = value.ThemeColors.PanelBorder, Surface = value.ThemeColors.Surface, SurfaceBorder = value.ThemeColors.SurfaceBorder, Footer = value.ThemeColors.Footer, TextPrimary = value.ThemeColors.TextPrimary, TextSecondary = value.ThemeColors.TextSecondary, Accent = value.ThemeColors.Accent, Hover = value.ThemeColors.Hover, IconSurface = value.ThemeColors.IconSurface },
        CustomThemes = value.CustomThemes.Select(profile => new ThemeProfile { Name = profile.Name, Colors = new ThemeColors { Panel = profile.Colors.Panel, PanelBorder = profile.Colors.PanelBorder, Surface = profile.Colors.Surface, SurfaceBorder = profile.Colors.SurfaceBorder, Footer = profile.Colors.Footer, TextPrimary = profile.Colors.TextPrimary, TextSecondary = profile.Colors.TextSecondary, Accent = profile.Colors.Accent, Hover = profile.Colors.Hover, IconSurface = profile.Colors.IconSurface } }).ToList(),
    };


    private void ToggleEditMode_Click(object sender, RoutedEventArgs e) => SetEditMode(!_isEditMode, null);

    private void SetEditMode(bool enabled, string? statusText)
    {
        _isEditMode = enabled;
        IsEditMode = enabled;
        EditHint.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        _viewModel.StatusText = statusText ?? (enabled ? "编辑模式：右键管理项目和分组" : "就绪");
    }

    private void GroupNavigation_GroupInvoked(object sender, GroupNavigationEventArgs e)
    {
        if (_viewModel.Settings.GroupSwitchMode != SettingsOptionValues.GroupSwitchClick)
        {
            return;
        }

        SyncSelectedGroupWithCoordinator();
        _groupSwitchCoordinator.RequestClick(e.Group);
    }

    private void GroupNavigation_GroupHovered(object sender, GroupNavigationEventArgs e)
    {
        if (_isEditMode ||
            _viewModel.Settings.GroupSwitchMode != SettingsOptionValues.GroupSwitchHover)
        {
            return;
        }

        SyncSelectedGroupWithCoordinator();
        if (e.IsActive)
        {
            _groupSwitchCoordinator.BeginHover(e.Group);
        }
        else
        {
            _groupSwitchCoordinator.CancelHover(e.Group);
        }
    }

    private void GroupNavigation_GroupDragHovered(object sender, GroupNavigationEventArgs e)
    {
        if (!_isEditMode || _dragStartedWhileFiltering)
        {
            return;
        }

        SyncSelectedGroupWithCoordinator();
        if (e.IsActive)
        {
            _groupSwitchCoordinator.BeginDragHover(e.Group);
        }
        else
        {
            _groupSwitchCoordinator.CancelDragHover(e.Group);
        }
    }

    private void GroupNavigation_GroupDropped(object sender, GroupNavigationEventArgs e)
    {
        _groupSwitchCoordinator.EndDrag();
        if (!_isEditMode || _dragStartedWhileFiltering ||
            _draggedItem is null || _dragSourceGroup is null)
        {
            return;
        }

        RestorePreviewOrder();
        MoveItem(_draggedItem, _dragSourceGroup, e.Group, e.Group.Items.Count);
        _draggedItem = null;
        _dragSourceGroup = null;
    }

    private void GroupSwitchCoordinator_SwitchRequested(
        object? sender,
        GroupSwitchRequestedEventArgs e)
    {
        if (e.Generation <= _latestGroupSwitchGeneration)
        {
            return;
        }

        _latestGroupSwitchGeneration = e.Generation;
        SelectGroup(e.Group, e.Generation);
    }

    private void SyncSelectedGroupWithCoordinator() =>
        _groupSwitchCoordinator.SelectedGroupId = _viewModel.SelectedGroup?.Id;

    private void GroupNavigation_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isContextMenuActivationPending = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            () => _isContextMenuActivationPending = false);

        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { DataContext: Group group } container)
        {
            return;
        }

        var contextMenu = BuildGroupContextMenu(group);
        contextMenu.Opened += GroupContextMenu_Opened;
        container.ContextMenu = contextMenu;
    }

    private void GroupContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        _isContextMenuActivationPending = false;
        _openContextMenus++;
    }

    private void SelectGroup(Group group, long generation)
    {
        if (string.Equals(_viewModel.SelectedGroup?.Id, group.Id, StringComparison.Ordinal))
        {
            SyncSelectedGroupWithCoordinator();
            return;
        }

        SaveCurrentViewState(_activeLayoutMode);
        _uiPerformanceTrace.GroupSwitchStarted(group.Id);

        _viewModel.SelectedGroup = group;
        _viewModel.SearchText = string.Empty;
        _groupSwitchCoordinator.SelectedGroupId = group.Id;
        _uiPerformanceTrace.SelectionAcknowledged(group.Id);
        RefreshEmptyState();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (generation != _latestGroupSwitchGeneration ||
                !string.Equals(_viewModel.SelectedGroup?.Id, group.Id, StringComparison.Ordinal))
            {
                return;
            }

            AttachVirtualizingPanel();
            RestoreViewState(reuseOffset: true);
            _uiPerformanceTrace.ContentStable(
                group.Id,
                _wrapPanel?.RealizedIndices.Count ?? ItemList.Items.Count);
        });
    }

    private void RefreshEmptyState()
    {
        EmptyPanel.Visibility = _viewModel.VisibleItems.Any() ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task LoadVisibleIconsAsync(ViewportRange? requestedRange = null)
    {
        if (_isClosed || !IsVisible || _viewModel.VisibleItems.Count == 0)
        {
            return;
        }

        AttachVirtualizingPanel();
        var viewport = requestedRange ?? GetCurrentIconViewport();
        var pixelSize = Math.Max(
            16,
            (int)Math.Ceiling(_viewModel.Settings.IconSize * VisualTreeHelper.GetDpi(this).DpiScaleX));
        var themeVariant = _viewModel.Settings.Theme;

        await _iconCoordinator.RefreshAsync(
            _viewModel.VisibleItems,
            viewport,
            pixelSize,
            themeVariant,
            default);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        if (_isClosed || !IsVisible || _viewModel.SelectedGroup is not { } selectedGroup)
        {
            return;
        }

        await _iconCoordinator.PreheatAsync(
            _viewModel.Config.Groups,
            selectedGroup.Id,
            pixelSize,
            themeVariant,
            default);
    }

    private ViewportRange GetCurrentIconViewport()
    {
        if (_viewModel.VisibleItems.Count == 0)
        {
            return ViewportRange.Empty;
        }

        if (_wrapPanel is { RealizedRange.FirstIndex: >= 0 } panel)
        {
            return panel.RealizedRange;
        }

        if (_activeLayoutMode == "list")
        {
            var realizedIndices = new List<int>();
            CollectRealizedItemIndices(ItemList, realizedIndices);
            if (realizedIndices.Count > 0)
            {
                return new ViewportRange(realizedIndices.Min(), realizedIndices.Max(), 1);
            }
        }

        return GetInitialIconViewport();
    }

    private ViewportRange GetInitialIconViewport()
    {
        var itemCount = _viewModel.VisibleItems.Count;
        var availableWidth = Math.Max(1, ItemList.ActualWidth - ItemList.Padding.Left - ItemList.Padding.Right);
        var availableHeight = Math.Max(1, ItemList.ActualHeight - ItemList.Padding.Top - ItemList.Padding.Bottom);
        int firstScreenCount;

        if (_activeLayoutMode == "list")
        {
            firstScreenCount = Math.Max(1, (int)Math.Ceiling(availableHeight / Math.Max(1, _viewModel.Settings.CardHeight)));
        }
        else
        {
            var itemWidth = Math.Max(1, _viewModel.Settings.CardWidth + _viewModel.Settings.ItemSpacing);
            var itemHeight = Math.Max(1, _viewModel.Settings.CardHeight + _viewModel.Settings.RowSpacing);
            var columns = Math.Max(1, (int)(availableWidth / itemWidth));
            var rows = Math.Max(1, (int)Math.Ceiling(availableHeight / itemHeight));
            firstScreenCount = columns * rows;
        }

        return new ViewportRange(0, Math.Min(itemCount, firstScreenCount) - 1, Math.Max(1, firstScreenCount));
    }

    private void CollectRealizedItemIndices(DependencyObject parent, ICollection<int> indices)
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(parent); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(parent, childIndex);
            if (child is ListBoxItem container)
            {
                var itemIndex = ItemList.ItemContainerGenerator.IndexFromContainer(container);
                if (itemIndex >= 0)
                {
                    indices.Add(itemIndex);
                }

                continue;
            }

            CollectRealizedItemIndices(child, indices);
        }
    }

    private void ItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_isEditMode && !_viewModel.Settings.OpenItemsOnSingleClick)
        {
            LaunchSelectedItem();
        }
    }

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
            _dragStartedWhileFiltering = !string.IsNullOrWhiteSpace(_viewModel.SearchText);
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
        if (sender is FrameworkElement { DataContext: LauncherItem item } fe)
        {
            ItemList.SelectedItem = item;
            fe.ContextMenu = BuildItemContextMenu(item);
        }
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

    private ContextMenu BuildItemContextMenu(LauncherItem item)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("LauncherContextMenu") };
        menu.Closed += LauncherContextMenu_Closed;
        var itemStyle = (Style)FindResource("LauncherMenuItem");

        var rename = new MenuItem { Header = "编辑名称", Style = itemStyle };
        rename.Click += RenameSelectedItem_Click;
        var edit = new MenuItem { Header = "编辑软件", Style = itemStyle };
        edit.Click += EditSelectedItem_Click;
        var remove = new MenuItem { Header = "从当前分组移除", Style = itemStyle };
        remove.Click += RemoveSelectedItem_Click;

        var moveMenu = new MenuItem { Header = "移动到分组", Style = itemStyle };
        var currentGroup = _viewModel.SelectedGroup;
        if (currentGroup != null && _viewModel.Config?.Groups != null)
        {
            foreach (var group in _viewModel.Config.Groups)
            {
                if (group == currentGroup) continue;
                var sub = new MenuItem { Header = group.Name, Tag = group, Style = itemStyle };
                sub.Click += MoveItemToGroupMenu_Click;
                moveMenu.Items.Add(sub);
            }
        }
        if (moveMenu.Items.Count == 0) moveMenu.IsEnabled = false;

        menu.Items.Add(rename);
        menu.Items.Add(edit);
        menu.Items.Add(remove);
        menu.Items.Add(moveMenu);
        return menu;
    }

    private void MoveItemToGroupMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Group targetGroup } || ItemList.SelectedItem is not LauncherItem item) return;
        var sourceGroup = _viewModel.SelectedGroup;
        if (sourceGroup is null || sourceGroup == targetGroup) return;
        MoveItem(item, sourceGroup, targetGroup, targetGroup.Items.Count);
    }

    private void DeleteButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Button button) button.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D));
    }

    private void DeleteButton_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Button button) button.Foreground = new SolidColorBrush(Colors.White);
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void LaunchSelectedItem_Click(object sender, RoutedEventArgs e) => LaunchSelectedItem();

    // 启动目标程序走 Windows Shell（UseShellExecute=true）。部分目标（Store 应用、需 COM
    // 激活、文件关联触发慢处理器的程序）会让 ShellExecuteEx 在调用线程上阻塞 2-3 秒。若直接跑在
    // UI 线程会导致面板卡死，因此先立即隐藏面板，再把实际启动放到后台线程，UI 全程不阻塞。
    private async void LaunchSelectedItem()
    {
        if (ItemList.SelectedItem is not LauncherItem item)
        {
            return;
        }

        // 先隐藏面板，避免 ShellExecute 阻塞期间界面卡顿。
        if (!_isEditMode)
        {
            Hide();
        }

        try
        {
            await Task.Run(() => _launcherService.Open(item.Path));
            item.UseCount++;
            _viewModel.RefreshVisibleItems();
            _viewModel.Save();
            _viewModel.StatusText = $"已启动：{item.Name}";
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

        var oldPath = item.Path;
        item.Name = dialog.ItemName;
        item.Path = dialog.ItemPath;
        item.IconImage = null;
        _iconService.Invalidate(oldPath);
        _iconService.Invalidate(item.Path);
        _viewModel.RefreshVisibleItems();
        _viewModel.Save();
    }

    private void RenameSelectedItem_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGroup is null || ItemList.SelectedItem is not LauncherItem item)
        {
            return;
        }

        var dialog = new Views.EditItemWindow(item.Name, item.Path, "编辑名称") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        item.Name = dialog.ItemName;
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

        if (_isEditMode && !_dragStartedWhileFiltering &&
            CanReorderSelectedGroup() &&
            e.Data.GetData(typeof(LauncherItem)) is LauncherItem item)
        {
            var position = e.GetPosition(ItemList);
            UpdateDragGhost(e.GetPosition(this));
            AutoScrollDuringDrag(position);
            PreviewItemOrder(item, GetLogicalInsertIndex(e));
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void ItemList_Drop(object sender, DragEventArgs e)
    {
        _groupSwitchCoordinator.EndDrag();
        if (_viewModel.SelectedGroup is null)
        {
            return;
        }

        if (_isEditMode && !_dragStartedWhileFiltering &&
            CanReorderSelectedGroup() &&
            e.Data.GetData(typeof(LauncherItem)) is LauncherItem item &&
            _dragSourceGroup is not null)
        {
            var targetIndex = GetLogicalInsertIndex(e);
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
                MoveItem(item, _dragSourceGroup, _viewModel.SelectedGroup, targetIndex);
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
            _groupSwitchCoordinator.EndDrag();
            RestorePreviewOrder();
            CloseDragGhost();
            _draggedItem = null;
            _dragSourceGroup = null;
            _dragSourceIndex = -1;
            _dragStartedWhileFiltering = false;
        }
    }

    private void ItemList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _groupSwitchCoordinator.EndDrag();
        _draggedItem = null;
        _dragSourceGroup = null;
        _dragSourceIndex = -1;
        _dragStartedWhileFiltering = false;

        if (!_isEditMode && _viewModel.Settings.OpenItemsOnSingleClick &&
            GetParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is LauncherItem item)
        {
            ItemList.SelectedItem = item;
            LaunchSelectedItem();
        }
    }

    private bool CanReorderSelectedGroup() =>
        _viewModel.SelectedGroup is not null &&
        string.IsNullOrWhiteSpace(_viewModel.SearchText) &&
        !string.Equals(_viewModel.SelectedGroup.SortMode, "frequency", StringComparison.Ordinal);

    private void PreviewItemOrder(LauncherItem item, int targetIndex)
    {
        if (_dragSourceGroup is null || _viewModel.SelectedGroup is null)
        {
            return;
        }

        _previewInsertIndex = targetIndex;
        if (!ReferenceEquals(_dragSourceGroup, _viewModel.SelectedGroup))
        {
            return;
        }

        var currentIndex = _viewModel.SelectedGroup.Items.IndexOf(item);
        targetIndex = Math.Clamp(targetIndex, 0, _viewModel.SelectedGroup.Items.Count);
        if (currentIndex < 0)
        {
            return;
        }

        if (currentIndex < targetIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(targetIndex, 0, _viewModel.SelectedGroup.Items.Count - 1);
        if (currentIndex == targetIndex)
        {
            return;
        }

        _viewModel.SelectedGroup.Items.Move(currentIndex, targetIndex);
        _previewTargetItem = item;
        _previewInsertIndex = targetIndex;
        _viewModel.RefreshVisibleItems();
    }

    private void RestorePreviewOrder()
    {
        if (_draggedItem is not null && _dragSourceGroup is not null && _dragSourceIndex >= 0)
        {
            var currentIndex = _dragSourceGroup.Items.IndexOf(_draggedItem);
            var targetIndex = Math.Clamp(_dragSourceIndex, 0, Math.Max(0, _dragSourceGroup.Items.Count - 1));
            if (currentIndex >= 0 && currentIndex != targetIndex)
            {
                _dragSourceGroup.Items.Move(currentIndex, targetIndex);
                if (ReferenceEquals(_dragSourceGroup, _viewModel.SelectedGroup))
                {
                    _viewModel.RefreshVisibleItems();
                }
            }
        }

        _previewTargetItem = null;
        _previewInsertIndex = -1;
    }

    private int GetLogicalInsertIndex(DragEventArgs e)
    {
        if (_viewModel.SelectedGroup is null)
        {
            return 0;
        }

        var visibleCount = _viewModel.VisibleItems.Count;
        if (visibleCount == 0)
        {
            return 0;
        }

        int visibleIndex;
        if (_activeLayoutMode != SettingsOptionValues.ListLayout && _wrapPanel is not null)
        {
            var point = e.GetPosition(_wrapPanel);
            var logicalPoint = new Point(
                point.X + _wrapPanel.HorizontalOffset,
                point.Y + _wrapPanel.VerticalOffset);
            var columns = Math.Max(1, _wrapPanel.RealizedRange.Columns);
            var layout = new VirtualizingWrapLayout(
                _wrapPanel.ItemWidth,
                _wrapPanel.ItemHeight,
                _wrapPanel.HorizontalSpacing,
                _wrapPanel.VerticalSpacing,
                _wrapPanel.BufferRows);
            visibleIndex = layout.IndexFromPoint(logicalPoint, visibleCount, columns);
            if (visibleIndex >= 0)
            {
                var itemRect = layout.GetItemRect(visibleIndex, columns);
                if (logicalPoint.X > itemRect.Left + (itemRect.Width / 2))
                {
                    visibleIndex++;
                }
            }
        }
        else if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { DataContext: LauncherItem targetItem } container)
        {
            visibleIndex = _viewModel.VisibleItems.IndexOf(targetItem);
            if (e.GetPosition(container).Y > container.ActualHeight / 2)
            {
                visibleIndex++;
            }
        }
        else
        {
            visibleIndex = visibleCount;
        }

        return MapVisibleInsertIndexToSource(visibleIndex);
    }

    private int MapVisibleInsertIndexToSource(int visibleIndex)
    {
        var group = _viewModel.SelectedGroup;
        if (group is null || visibleIndex >= _viewModel.VisibleItems.Count)
        {
            return group?.Items.Count ?? 0;
        }

        visibleIndex = Math.Clamp(visibleIndex, 0, _viewModel.VisibleItems.Count - 1);
        var sourceIndex = group.Items.IndexOf(_viewModel.VisibleItems[visibleIndex]);
        return sourceIndex >= 0 ? sourceIndex : group.Items.Count;
    }

    private void AutoScrollDuringDrag(Point position)
    {
        var delta = position.Y < DragAutoScrollEdge
            ? -DragAutoScrollStep
            : position.Y > ItemList.ActualHeight - DragAutoScrollEdge
                ? DragAutoScrollStep
                : 0;
        if (delta == 0)
        {
            return;
        }

        if (_wrapPanel is not null && _activeLayoutMode != SettingsOptionValues.ListLayout)
        {
            _wrapPanel.SetVerticalOffset(_wrapPanel.VerticalOffset + delta);
            return;
        }

        _itemScrollViewer?.ScrollToVerticalOffset(Math.Max(0, _itemScrollViewer.VerticalOffset + delta));
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
                BorderBrush = (Brush)FindResource("DragIndicatorBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Opacity = 1,
                Child = new StackPanel
                {
                    Children =
                    {
                        new Image { Source = item.IconImage as System.Windows.Media.ImageSource, Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Center },
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
