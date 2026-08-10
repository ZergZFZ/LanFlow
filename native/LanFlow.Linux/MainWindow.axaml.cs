using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;
using LanFlow.Desktop.ViewModels;
using LanFlow.Linux;

namespace LanFlow.Desktop;

public sealed partial class MainWindow : Window
{
    // 确保 libX11 的 DllImportResolver 已注册（注册在 HotkeyService 静态构造中），
    // 否则本窗口的 XOpenDisplay/XGetInputFocus 在未触发 HotkeyService 实例化时会 DllNotFound。
    static MainWindow()
    {
        _ = typeof(HotkeyService);
    }

    private MainViewModel _viewModel = null!;
    private readonly LauncherService _launcher = new();
    private readonly ShellIconService _shellIcon = new();
    private HotkeyService? _hotkey;
    private bool _editMode;
    private bool _exiting;
    private LauncherItem? _dragItem;
    private Point _dragStart;
    private bool _isDragging;
    private int _modalDepth;
    private int _searchIndex;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new ConfigStore("Ctrl+Alt+L"));
        DataContext = _viewModel;

        // Linux/X11 下避免使用 Transparent 级别的 TransparencyLevelHint，
        // 否则调整窗口大小会触发 X11 窗口 visual 重建导致卡死。
        // 使用 None 作为安全回退。
        TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
        App.ApplyThemeColors(_viewModel.Settings);
        ApplyMetrics(_viewModel.Settings);
        BuildGroupTabs();
        ReloadItems();

        Closing += OnClosing;
        Deactivated += OnDeactivated;

        // 第三轮取证件（缺陷板 v2 §3.3）：窗口打开 500ms 后 dump 分组栏渲染结果
        Opened += (_, _) =>
        {
            // B2-1：启动后 2 秒内抑制失焦隐藏（窗口显示时焦点尚未稳定，否则一打开就被隐藏）
            _suppressHideUntilUtc = DateTime.UtcNow.AddSeconds(2);
            StartFocusCheck();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                DumpLayoutForensics();
            };
            timer.Start();
        };
    }

    /// <summary>第三轮取证件：dump 主窗口与分组栏尺寸。若计数为 0 或 Bounds 为零，日志可直接点名根因。</summary>
    private void DumpLayoutForensics()
    {
        try
        {
            Console.WriteLine($"[取证] MainWindow Bounds={Bounds}, GroupTabs.Children.Count={GroupTabs.Children.Count}");
            var index = 0;
            foreach (var child in GroupTabs.Children)
            {
                Console.WriteLine($"[取证] GroupTabs[{index}] type={child.GetType().Name} Bounds={child.Bounds} IsVisible={child.IsVisible}");
                index++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[取证] MainWindow dump 失败: " + ex);
        }
    }

    public void EnableHotkey()
    {
        _hotkey = new HotkeyService();
        var registered = _hotkey.Register(this, () => Dispatcher.UIThread.Post(ToggleVisibility), _viewModel.Settings.Hotkey);
        if (!registered)
        {
            var message = string.IsNullOrEmpty(_hotkey.LastError) ? "全局热键不可用" : _hotkey.LastError;
            _viewModel.StatusText = message;
            Console.WriteLine("[LanFlow] 全局热键注册失败: " + message);
        }
        else
        {
            Console.WriteLine("[LanFlow] 全局热键注册成功: " + _viewModel.Settings.Hotkey);
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
            // B2-1：热键唤回后 800ms 内抑制失焦隐藏（Activate 焦点切换异步，避免被紧随的失焦吞掉）
            _suppressHideUntilUtc = DateTime.UtcNow.AddMilliseconds(800);
            Show();
            Activate();
            // Deepin/X11 下 Activate 可能不立即抢到焦点（焦点策略为"点击激活"时，无焦点的窗口
            // 从未触发 Deactivated，导致点桌面不隐藏）。短暂置顶强制 WM 给焦点。
            Topmost = true;
            var restoreTopmost = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            restoreTopmost.Tick += (_, _) =>
            {
                restoreTopmost.Stop();
                Topmost = false;
            };
            restoreTopmost.Start();
        }
    }

    public void Quit()
    {
        _exiting = true;
        _focusTimer?.Stop();
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

    /// <summary>B2-1 失焦隐藏：无模态对话框且设置开启时，失焦自动隐藏（托盘常驻）。</summary>
    /// X11 下 Deactivated 事件依赖"窗口曾激活→失激活"，而快捷键呼出的窗口在 Deepin 上可能拿不到焦点
    /// （从未激活，点桌面/切窗口都不触发 Deactivated）。因此用 XGetInputFocus 主动轮询：
    /// 焦点不在本窗口即隐藏。启动/热键唤回后的短暂失焦仍用 _suppressHideUntilUtc 抑制。
    private DateTime _suppressHideUntilUtc = DateTime.MinValue;
    private DispatcherTimer? _focusTimer;

    private void OnDeactivated(object? sender, EventArgs e) => TryAutoHide();

    private void TryAutoHide()
    {
        if (_modalDepth > 0 || !_viewModel.Settings.HideOnDeactivate)
        {
            return;
        }

        if (DateTime.UtcNow < _suppressHideUntilUtc)
        {
            return;
        }

        Hide();
    }

    /// <summary>轮询 X11 焦点：焦点不在本窗口且非模态时自动隐藏。</summary>
    private void StartFocusCheck()
    {
        if (_focusTimer != null)
        {
            return;
        }

        _focusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _focusTimer.Tick += (_, _) =>
        {
            if (!_viewModel.Settings.HideOnDeactivate) return; // 开关关闭零 X 开销
            if (_modalDepth > 0) return;                        // 模态对话框不隐藏
            if (DateTime.UtcNow < _suppressHideUntilUtc) return;
            if (IsSelfFocused()) return;
            Hide();
        };
        _focusTimer.Start();
    }

    /// <summary>X11 当前焦点是否在本应用（按 _NET_WM_PID == 当前进程 PID 判定）。
    /// 查询失败时保守返回 true（不误隐藏）。</summary>
    private bool IsSelfFocused()
    {
        try
        {
            var display = XOpenDisplay(null);
            if (display == IntPtr.Zero)
            {
                return true;
            }

            try
            {
                XGetInputFocus(display, out var focus, out _);
                return focus != IntPtr.Zero && GetWindowPid(display, focus) == Environment.ProcessId;
            }
            finally
            {
                XCloseDisplay(display);
            }
        }
        catch
        {
            return true;
        }
    }

    private static int GetWindowPid(IntPtr display, IntPtr window)
    {
        try
        {
            var pidAtom = XInternAtom(display, "_NET_WM_PID", true);
            if (pidAtom == IntPtr.Zero)
            {
                return 0;
            }

            const int XaCardinal = 6;
            if (XGetWindowProperty(display, window, pidAtom, 0, 1, false, XaCardinal,
                    out _, out _, out _, out _, out var data) != 0 || data == IntPtr.Zero)
            {
                return 0;
            }

            try
            {
                return Marshal.ReadInt32(data);
            }
            finally
            {
                XFree(data);
            }
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>模态对话框守卫：对话框打开期间不触发失焦隐藏（ShowDialog 会让主窗口 Deactivated）。</summary>
    private async Task<T> ShowDialogGuarded<T>(T dialog) where T : Window
    {
        _modalDepth++;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            _modalDepth--;
        }

        return dialog;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _searchIndex = 0;
        _viewModel.SearchText = SearchBox.Text ?? string.Empty;
        ReloadItems();
    }

    /// <summary>B2-6 搜索键盘操作：Enter 启动、上下键遍历结果、Esc 清空。</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = string.Empty;
            e.Handled = true;
            return;
        }

        var items = _viewModel.VisibleItems.ToList();
        if (items.Count == 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                LaunchItem(items[Math.Clamp(_searchIndex, 0, items.Count - 1)]);
                e.Handled = true;
                break;
            case Key.Down:
                _searchIndex = Math.Min(_searchIndex + 1, items.Count - 1);
                e.Handled = true;
                break;
            case Key.Up:
                _searchIndex = Math.Max(_searchIndex - 1, 0);
                e.Handled = true;
                break;
        }
    }

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new Views.SettingsWindow(_viewModel);
        settingsWindow.OnApplied = RefreshAfterSettings;
        await ShowDialogGuarded(settingsWindow);
    }

    private void RefreshAfterSettings()
    {
        App.ApplyThemeColors(_viewModel.Settings);
        ApplyMetrics(_viewModel.Settings);
        // B3-6：设置变更后清图标缓存，按当前主题/图标解析重新加载
        ShellIconService.Clear();
        BuildGroupTabs();
        ReloadItems();
        if (_hotkey?.TryRegister(_viewModel.Settings.Hotkey) == false)
        {
            Console.WriteLine("[LanFlow] 热键重注册失败: " + _hotkey.LastError);
        }
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

        // B3-2 透明度双模式：整窗 = 窗口整体 Opacity；分层 = 仅项目区内容透明
        // （窗口自身保持不透明，避免 X11 visual 重建卡死——D6 教训）
        if (settings.TransparencyMode == "wholeWindow")
        {
            Opacity = Math.Clamp(settings.WholeWindowOpacity, 0.55, 1.0);
            if (ItemsHost is not null)
            {
                ItemsHost.Opacity = 1;
            }
        }
        else
        {
            Opacity = 1;
            if (ItemsHost is not null)
            {
                ItemsHost.Opacity = Math.Clamp(settings.LayeredOpacity, 0.55, 1.0);
            }
        }
    }

    /// <summary>
    /// D1 根因修复：应用级画刷定义在 App.axaml / ApplyThemeColors（Application.Resources），
    /// 窗口本地 Resources 字典取不到（返回 null 导致分组标签隐形）。
    /// TryGetResource 会沿资源树向上解析到 Application。
    /// </summary>
    private SolidColorBrush? GetBrush(string key)
    {
        if (this.TryGetResource(key, out var value) && value is SolidColorBrush brush)
        {
            return brush;
        }

        // 兜底：窗口资源父链未挂到 Application 时（个别宿主/时序下），直接读应用级字典
        return Application.Current?.Resources[key] as SolidColorBrush;
    }

    private void BuildGroupTabs()
    {
        GroupTabs.Children.Clear();
        Console.WriteLine($"[LanFlow] 渲染分组栏: {_viewModel.Groups.Count()} 个分组");
        foreach (var group in _viewModel.Groups)
        {
            try
            {
                var isSelected = group == _viewModel.SelectedGroup;
                var border = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Background = isSelected ? GetBrush("AccentBrush") : GetBrush("SurfaceBrush"),
                    BorderBrush = GetBrush("SurfaceBorderBrush"),
                    BorderThickness = new Thickness(1),
                };
                // B1-4：编辑模式显示分组上移/下移按钮（手动排序持久化）
                var tabContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                tabContent.Children.Add(new TextBlock
                {
                    Text = group.Name,
                    Foreground = GetBrush("TextPrimaryBrush"),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                if (_editMode)
                {
                    var up = new Button
                    {
                        Content = "↑", Width = 20, Height = 20, Padding = new Thickness(0), FontSize = 10,
                    };
                    up.Click += (_, _) => OnMoveGroupUp(group);
                    tabContent.Children.Add(up);

                    var down = new Button
                    {
                        Content = "↓", Width = 20, Height = 20, Padding = new Thickness(0), FontSize = 10,
                    };
                    down.Click += (_, _) => OnMoveGroupDown(group);
                    tabContent.Children.Add(down);
                }
                border.Child = tabContent;
                border.PointerPressed += (_, _) => SelectGroup(group);

                // B2-4 悬停切换：设置开启时，悬停停留达延迟后自动切换分组
                if (_viewModel.Settings.GroupSwitchMode == "hover")
                {
                    var hoverTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(Math.Max(0, _viewModel.Settings.GroupHoverDelayMs)),
                    };
                    hoverTimer.Tick += (_, _) =>
                    {
                        hoverTimer.Stop();
                        SelectGroup(group);
                    };
                    border.PointerEntered += (_, _) =>
                    {
                        if (group != _viewModel.SelectedGroup)
                        {
                            hoverTimer.Start();
                        }
                    };
                    border.PointerExited += (_, _) => hoverTimer.Stop();
                }

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
            catch (Exception ex)
            {
                Console.WriteLine("[LanFlow] 分组「" + group.Name + "」渲染失败: " + ex);
            }
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
        var items = _viewModel.VisibleItems.ToList();
        LoadIcons(items);
        ItemsControl.ItemsSource = items;
        EmptyStateText.IsVisible = items.Count == 0;
        EmptyStateText.Text = string.IsNullOrWhiteSpace(_viewModel.SearchText)
            ? "该分组暂无项目\n点击「添加文件」或进入编辑模式添加"
            : "未找到匹配的项目";
    }

    private void LoadIcons(IReadOnlyList<LauncherItem> items)
    {
        foreach (var item in items)
        {
            if (item.IconImage is null)
            {
                item.IconImage = _shellIcon.GetIcon(item);
            }
        }
    }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        // 右键：弹出项目菜单（B4-1），不进入编辑/拖拽流程
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (control.DataContext is LauncherItem ctxItem)
            {
                ShowItemContextMenu(control, ctxItem);
            }

            return;
        }

        if (!_editMode)
        {
            return;
        }

        // 排序/删除按钮的点击不进入拖拽或编辑流程，避免按钮 Click 与 Border 手势冲突
        if (e.Source is Button)
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
            var list = paths?.ToList() ?? new List<string>();
            Console.WriteLine($"[LanFlow] 主区收到外部拖入 {list.Count} 个文件");
            if (list.Count > 0)
            {
                DropFiles(list, _viewModel.SelectedGroup);
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
            var list = paths?.ToList() ?? new List<string>();
            Console.WriteLine($"[LanFlow] 分组标签「{targetGroup.Name}」收到外部拖入 {list.Count} 个文件");
            if (list.Count > 0)
            {
                DropFiles(list, targetGroup);
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

            // 去重：同一分组内已存在的路径跳过（拖放/批量添加/目录导入共用此入口）
            if (group.Items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
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
        if (sender is Control control && control.DataContext is LauncherItem item)
        {
            DeleteItem(item);
        }
    }

    private void DeleteItem(LauncherItem item)
    {
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

    // B4-1：项目右键菜单。Avalonia 的 ContextMenu 内 MenuItem 不支持 XAML 事件绑定，
    // 因此在 code-behind 动态构建菜单并挂 Click。
    private void ShowItemContextMenu(Control control, LauncherItem item)
    {
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = "打开", Tag = item },
                new MenuItem { Header = "编辑", Tag = item },
                new MenuItem { Header = "删除", Tag = item },
                new Separator(),
                new MenuItem { Header = "上移", Tag = item },
                new MenuItem { Header = "下移", Tag = item },
            }
        };

        foreach (var entry in menu.Items)
        {
            if (entry is MenuItem mi)
            {
                mi.Click += OnContextMenuItemClick;
            }
        }

        menu.Open(control);
    }

    private void OnContextMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not LauncherItem item)
        {
            return;
        }

        switch (mi.Header as string)
        {
            case "打开":
                LaunchItem(item);
                break;
            case "编辑":
                OpenEditor(item);
                break;
            case "删除":
                DeleteItem(item);
                break;
            case "上移":
                MoveItem(item, -1);
                break;
            case "下移":
                MoveItem(item, +1);
                break;
        }
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
            var target = item.IsCommand && !string.IsNullOrWhiteSpace(item.Command) ? item.Command : item.Path;
            Console.WriteLine("[LanFlow] 启动: " + item.DisplayName + " -> " + target);
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
            Console.WriteLine("[LanFlow] 启动失败: " + item.DisplayName + " -> " + ex);
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
        await ShowDialogGuarded(dialog);
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

        var item = new LauncherItem { Name = string.Empty, Kind = "app" };
        var dialog = new Views.EditItemWindow();
        dialog.InitializeDialog(item);
        await ShowDialogGuarded(dialog);
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

    /// <summary>B1-1：批量添加文件（多选），自动命名并直接入库，不逐个弹编辑框。</summary>
    private async void OnAddFilesBatch(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "批量添加文件（可多选）",
                AllowMultiple = true,
            });

            var paths = files.Select(f => f.Path.LocalPath).ToList();
            Console.WriteLine($"[LanFlow] 批量添加 {paths.Count} 个文件");
            if (paths.Count > 0)
            {
                DropFiles(paths, _viewModel.SelectedGroup);
            }
            else
            {
                _viewModel.StatusText = "未选择文件";
            }
        }
        catch (Exception ex)
        {
            // UOS 上 DBus portal 缺失时 OpenFilePicker 可能抛异常，必须落日志不崩溃
            _viewModel.StatusText = "批量添加失败：" + ex.Message;
            Console.WriteLine("[LanFlow] 批量添加异常: " + ex);
        }
    }

    /// <summary>B1-1：从目录导入——扫描目录内 .desktop 与可执行文件，批量加入当前分组。</summary>
    private async void OnImportDirectory(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dirs = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择要导入的目录",
                AllowMultiple = false,
            });
            if (dirs.Count == 0)
            {
                return;
            }

            var dir = dirs[0].Path.LocalPath;
            var paths = new List<string>();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (paths.Count >= 500)
                    {
                        break;
                    }

                    if (IsImportableFile(file))
                    {
                        paths.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LanFlow] 目录扫描失败: " + ex);
            }

            Console.WriteLine($"[LanFlow] 从目录导入 {paths.Count} 个条目: {dir}");
            if (paths.Count > 0)
            {
                DropFiles(paths, _viewModel.SelectedGroup);
            }
            else
            {
                _viewModel.StatusText = "目录内未找到可导入的应用";
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "目录导入失败：" + ex.Message;
            Console.WriteLine("[LanFlow] 目录导入异常: " + ex);
        }
    }

    /// <summary>目录导入的条目判定：.desktop 全收；普通文件要求具备可执行位（兼容 glibc 2.28 的 UOS）。</summary>
    private static bool IsImportableFile(string path)
    {
        if (path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode anyExec =
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (mode & anyExec) != 0;
        }
        catch
        {
            // Windows 探针/开发机无有效 Unix 权限位，按常见可执行扩展名兜底
            return path.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".run", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>B1-4：项目上移（组内手动排序，顺序随配置持久化）。</summary>
    private void OnMoveItemUp(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && control.DataContext is LauncherItem item)
        {
            MoveItem(item, -1);
        }
    }

    /// <summary>B1-4：项目下移（组内手动排序，顺序随配置持久化）。</summary>
    private void OnMoveItemDown(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && control.DataContext is LauncherItem item)
        {
            MoveItem(item, +1);
        }
    }

    /// <summary>组内移动项目并持久化；delta=-1 上移，+1 下移。</summary>
    private void MoveItem(LauncherItem item, int delta)
    {
        var group = _viewModel.SelectedGroup;
        if (group is null)
        {
            return;
        }

        var index = group.Items.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= group.Items.Count)
        {
            return;
        }

        group.Items.Move(index, target);
        SaveAfterChange((delta < 0 ? "已上移：" : "已下移：") + item.DisplayName);
    }

    /// <summary>B1-4：分组上移/下移（分组标签顺序持久化）。</summary>
    private void MoveGroup(Group group, int delta)
    {
        var index = _viewModel.Config.Groups.IndexOf(group);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _viewModel.Config.Groups.Count)
        {
            return;
        }

        _viewModel.Config.Groups.Move(index, target);
        SaveAfterChange("已调整分组顺序");
        BuildGroupTabs();
    }

    private void OnMoveGroupUp(Group group) => MoveGroup(group, -1);

    private void OnMoveGroupDown(Group group) => MoveGroup(group, 1);

    private void SaveAfterChange(string status)
    {
        try
        {
            _viewModel.Save();
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "保存失败：" + ex.Message;
        }

        ReloadItems();
        _viewModel.StatusText = status;
    }

    private async void OpenEditor(LauncherItem item)
    {
        var dialog = new Views.EditItemWindow();
        dialog.InitializeDialog(item);
        await ShowDialogGuarded(dialog);
        if (dialog.Confirmed)
        {
            item.IconImage = null;
            _viewModel.Save();
            ReloadItems();
        }
    }

    // ---- B2-1 失焦隐藏：X11 焦点轮询（libX11 加载走 HotkeyService 的 DllImportResolver）----
    [DllImport("libX11")]
    private static extern IntPtr XOpenDisplay(string? display);

    [DllImport("libX11")]
    private static extern int XGetInputFocus(IntPtr display, out IntPtr focusReturn, out int revertToReturn);

    [DllImport("libX11")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11")]
    private static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

    [DllImport("libX11")]
    private static extern int XGetWindowProperty(
        IntPtr display, IntPtr window, IntPtr property, IntPtr longOffset, IntPtr longLength,
        bool delete, int reqType, out IntPtr actualType, out int actualFormat,
        out IntPtr nitems, out IntPtr bytesAfter, out IntPtr prop);

    [DllImport("libX11")]
    private static extern int XFree(IntPtr data);
}
