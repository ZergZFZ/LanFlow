using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LanFlow.Desktop.Services;
using Microsoft.Win32;

namespace LanFlow.Desktop.Views;

/// <summary>
/// 全屏截图窗口：框选截图 → 自动复制原图 → 弹出编辑快捷条（箭头/框选/颜色/马赛克/撤销/另存为/完成）。
/// 坐标约定：窗口内为 DIP；合成图按 _scale 渲染为物理像素尺寸。
/// </summary>
public partial class ScreenshotCropWindow : Window
{
    private enum EditTool { None, Arrow, Rect, Mosaic }

    private static readonly Brush MaskBrush = new SolidColorBrush(Color.FromArgb(0xC0, 0x00, 0x00, 0x00));
    private static readonly Brush SelectionBorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0x9A, 0xFF));
    private static readonly Brush WindowHoverBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x2F, 0xD4, 0x6B));
    private const double MinSelectionPixels = 4;
    private const int MosaicBlockPx = 16;
    private static string? _saveDirectory; // 进程级（本次启动会话）：另存为目录记忆

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xE8, 0x45, 0x3B), // 红
        Color.FromRgb(0xF5, 0xA6, 0x23), // 橙
        Color.FromRgb(0xF5, 0xD9, 0x2B), // 黄
        Color.FromRgb(0x35, 0xC4, 0x6B), // 绿
        Color.FromRgb(0x4C, 0x9A, 0xFF), // 蓝
        Color.FromRgb(0x9B, 0x59, 0xF6), // 紫
        Color.FromRgb(0xFF, 0xFF, 0xFF), // 白
        Color.FromRgb(0x1F, 0x24, 0x30), // 黑
    ];

    private readonly ScreenShotResult _shot;
    private double _scale; // 物理像素 / DIP：按窗口实际尺寸实测，规避分层窗口 DPI 缩放失效
    private readonly List<UIElement> _annotations = [];
    private readonly List<Rect> _mosaicRects = [];
    private readonly Stack<Action> _undoStack = [];

    private Point _start;
    private Point _current;
    private bool _isDrawing;
    private string? _resizeHandle;  // 当前拖动的手柄方向（n/s/e/w/nw/ne/se/sw）
    private Point _resizeAnchorPos; // 按下手柄时的鼠标位置（锚点）
    private Rect _resizeAnchorRect; // 按下手柄时的选区（锚点）
    private Rect _editRect;         // 上次编辑选区（用于计算标注平移增量）
    private bool _isEditing;
    private EditTool _currentTool = EditTool.None;
    private int _colorIndex;
    private UIElement? _draftElement;

    public ScreenshotCropWindow(ScreenShotResult shot)
    {
        InitializeComponent();
        _shot = shot;
        _scale = 1.0; // 窗口物理尺寸确定后由 OnSourceInitialized 实测

        FrozenImage.Source = shot.Image;

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        EditCanvas.MouseLeftButtonDown += OnEditMouseDown;
        EditCanvas.MouseMove += OnEditMouseMove;
        EditCanvas.MouseLeftButtonUp += OnEditMouseUp;
        UpdateColorButton();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // 分层窗口（AllowsTransparency）在 RDP/高 DPI 下 WPF 的 DIP→物理换算会失效，
        // 导致窗口只占物理屏幕左上角。这里直接用物理像素强制覆盖整个虚拟屏幕。
        const int swpNoActivate = 0x0010;
        const int swpShowWindow = 0x0040;
        SetWindowPos(
            hwnd,
            new IntPtr(-1), // HWND_TOPMOST
            _shot.VirtualBounds.X,
            _shot.VirtualBounds.Y,
            _shot.VirtualBounds.Width,
            _shot.VirtualBounds.Height,
            swpNoActivate | swpShowWindow);

        // 布局完成后按窗口实际尺寸实测缩放比（物理像素 / DIP）。
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            UpdateLayout();
            if (ActualWidth > 0)
            {
                _scale = _shot.VirtualBounds.Width / ActualWidth;
            }
            RenderSelection();
        });

        Activate();
        Focus();
    }

    // ---------- 框选阶段 ----------

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isEditing || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = Clamp(e.GetPosition(OverlayCanvas));
        _isDrawing = true;
        _start = pos;
        _current = pos;
        RenderSelection();
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = Clamp(e.GetPosition(OverlayCanvas));
        if (_resizeHandle is not null)
        {
            ApplyResize(_resizeHandle, pos);
            if (_isEditing) RefreshEditFromSelection(); else RenderSelection();
            return;
        }
        if (_isEditing || !_isDrawing) return;
        _current = pos;
        RenderSelection();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizeHandle is not null)
        {
            _resizeHandle = null;
            ReleaseMouseCapture();
            if (_isEditing) RefreshEditFromSelection(); else RenderSelection();
            return;
        }
        if (_isEditing || !_isDrawing) return;

        _isDrawing = false;
        ReleaseMouseCapture();
        _current = Clamp(e.GetPosition(OverlayCanvas));
        ConfirmSelection(); // 框选完成直接进入编辑模式，无需再确认
    }

    /// <summary>按手柄方向基于锚点增量调整选区边界，禁止翻转并保证最小尺寸。</summary>
    private void ApplyResize(string dir, Point pos)
    {
        var dx = pos.X - _resizeAnchorPos.X;
        var dy = pos.Y - _resizeAnchorPos.Y;
        var rect = _resizeAnchorRect;
        double left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;
        if (dir.Contains('w')) left = rect.Left + dx;
        if (dir.Contains('e')) right = rect.Right + dx;
        if (dir.Contains('n')) top = rect.Top + dy;
        if (dir.Contains('s')) bottom = rect.Bottom + dy;

        const double min = 8; // DIP 最小边
        if (right - left < min) { if (dir.Contains('e')) right = left + min; else left = right - min; }
        if (bottom - top < min) { if (dir.Contains('s')) bottom = top + min; else top = bottom - min; }
        _start = new Point(left, top);
        _current = new Point(right, bottom);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    private Point Clamp(Point p) => new(
        Math.Clamp(p.X, 0, Math.Max(1, OverlayCanvas.ActualWidth)),
        Math.Clamp(p.Y, 0, Math.Max(1, OverlayCanvas.ActualHeight)));

    // ---------- 框选渲染 ----------

    private Rect SelectionRect => new(
        Math.Min(_start.X, _current.X),
        Math.Min(_start.Y, _current.Y),
        Math.Abs(_current.X - _start.X),
        Math.Abs(_current.Y - _start.Y));

    private void RenderSelection()
    {
        OverlayCanvas.Children.Clear();
        if (_isEditing) return; // 编辑模式由 EnterEditMode/RefreshEditFromSelection 维护蒙版与手柄

        var rect = SelectionRect;
        var w = OverlayCanvas.ActualWidth;
        var h = OverlayCanvas.ActualHeight;

        if (rect.Width > 0 || rect.Height > 0)
        {
            AddMask(new Rect(0, 0, w, rect.Y));
            AddMask(new Rect(0, rect.Bottom, w, Math.Max(0, h - rect.Bottom)));
            AddMask(new Rect(0, rect.Y, rect.X, rect.Height));
            AddMask(new Rect(rect.Right, rect.Y, Math.Max(0, w - rect.Right), rect.Height));

            var frame = new Rectangle
            {
                Fill = Brushes.Transparent,
                Stroke = SelectionBorderBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Width = rect.Width,
                Height = rect.Height,
            };
            Canvas.SetLeft(frame, rect.X);
            Canvas.SetTop(frame, rect.Y);
            OverlayCanvas.Children.Add(frame);

            var px = (int)Math.Round(rect.Width * _scale);
            var py = (int)Math.Round(rect.Height * _scale);
            var size = new TextBlock
            {
                Text = $"{px} × {py}",
                Foreground = Brushes.White,
                FontSize = 13,
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00)),
                Padding = new Thickness(6, 2, 6, 2),
            };
            var x = rect.X + 2;
            var y = rect.Y - 24 >= 0 ? rect.Y - 24 : rect.Bottom + 2;
            Canvas.SetLeft(size, x);
            Canvas.SetTop(size, y);
            OverlayCanvas.Children.Add(size);

            if (!_isDrawing) RenderHandles(rect);
        }
    }

    private static readonly Brush HandleBrush =
        new SolidColorBrush(Color.FromArgb(0xFF, 0x2F, 0xD4, 0x6B));

    /// <summary>在 HandleCanvas（编辑框之上）渲染绿色虚线框 + 四边中点/四角共 8 个手柄。</summary>
    private void RenderHandles(Rect rect)
    {
        HandleCanvas.Children.Clear();

        var frame = new Rectangle
        {
            Fill = Brushes.Transparent,
            Stroke = HandleBrush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            IsHitTestVisible = false,
            Width = rect.Width,
            Height = rect.Height,
        };
        Canvas.SetLeft(frame, rect.X);
        Canvas.SetTop(frame, rect.Y);
        HandleCanvas.Children.Add(frame);

        const double size = 10;
        var points = new (string Dir, Point Pos)[]
        {
            ("nw", rect.TopLeft),
            ("n", new Point(rect.X + rect.Width / 2, rect.Y)),
            ("ne", rect.TopRight),
            ("e", new Point(rect.Right, rect.Y + rect.Height / 2)),
            ("se", rect.BottomRight),
            ("s", new Point(rect.X + rect.Width / 2, rect.Bottom)),
            ("sw", rect.BottomLeft),
            ("w", new Point(rect.X, rect.Y + rect.Height / 2)),
        };
        foreach (var (dir, pos) in points)
        {
            var handle = new Rectangle
            {
                Width = size,
                Height = size,
                Fill = HandleBrush,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Tag = dir,
                Cursor = Cursors.Hand, // 手型提示可拖拽；点击已 Handled，不会误触发重新框选
            };
            Canvas.SetLeft(handle, pos.X - size / 2);
            Canvas.SetTop(handle, pos.Y - size / 2);
            handle.MouseLeftButtonDown += Handle_MouseLeftButtonDown;
            HandleCanvas.Children.Add(handle);
        }
    }

    private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle { Tag: string dir }) return;
        // 记录拖拽锚点（按下时的鼠标位置与选区），拖动时按增量调整，
        // 避免点击瞬间用鼠标绝对坐标覆盖边导致选区跳变。
        // 编辑模式下 _start/_current 被标注绘制复用，选区锚点必须取 _editRect。
        _resizeHandle = dir;
        _resizeAnchorPos = Clamp(e.GetPosition(OverlayCanvas));
        _resizeAnchorRect = _isEditing ? _editRect : SelectionRect;
        e.Handled = true;
        CaptureMouse();
    }

    private void AddMask(Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var mask = new Rectangle
        {
            Fill = MaskBrush,
            Width = rect.Width,
            Height = rect.Height,
        };
        Canvas.SetLeft(mask, rect.X);
        Canvas.SetTop(mask, rect.Y);
        OverlayCanvas.Children.Add(mask);
    }

    private void ConfirmSelection()
    {
        var rect = SelectionRect;
        if (rect.Width * _scale < MinSelectionPixels || rect.Height * _scale < MinSelectionPixels)
        {
            return; // 选区过小视为无效，保留窗口等待重新框选。
        }

        _resizeHandle = null;

        var pixelRect = new Int32Rect(
            (int)Math.Round(_shot.VirtualBounds.X + rect.X * _scale),
            (int)Math.Round(_shot.VirtualBounds.Y + rect.Y * _scale),
            (int)Math.Round(rect.Width * _scale),
            (int)Math.Round(rect.Height * _scale));

        // 1) 自动复制原图（第一批行为）。
        if (!ScreenshotService.CopySelection(_shot, pixelRect))
        {
            return;
        }

        // 2) 进入编辑模式：工作区 + 快捷条。
        EnterEditMode(rect, pixelRect);
    }

    /// <summary>编辑模式下按手柄调整选区后，重建编辑工作区（裁剪图/蒙版/工具条/手柄），保留已有标注。</summary>
    private void RefreshEditFromSelection()
    {
        var rect = SelectionRect;
        if (rect.Width * _scale < MinSelectionPixels || rect.Height * _scale < MinSelectionPixels)
        {
            return;
        }

        var pixelRect = new Int32Rect(
            (int)Math.Round(_shot.VirtualBounds.X + rect.X * _scale),
            (int)Math.Round(_shot.VirtualBounds.Y + rect.Y * _scale),
            (int)Math.Round(rect.Width * _scale),
            (int)Math.Round(rect.Height * _scale));

        // 重新裁剪并替换编辑工作区内容。
        var relative = new Int32Rect(
            pixelRect.X - _shot.VirtualBounds.X,
            pixelRect.Y - _shot.VirtualBounds.Y,
            pixelRect.Width,
            pixelRect.Height);
        var cropped = new CroppedBitmap(_shot.Image, relative);
        cropped.Freeze();

        // 选区左上角发生移动时，平移已有标注元素（相对原图锚定），保证标注与内容不脱节。
        // 注意：不能整体平移 EditCanvas（RenderTransform），否则命中测试区域跟着偏移，
        // 导致向新扩展区域（左/上）无法绘制。改为对每个标注元素自身累加 TranslateTransform
        // （仅平移该标注，EditCanvas 命中区域不受影响）。
        var shift = new Vector(_editRect.X - rect.X, _editRect.Y - rect.Y);
        if (shift.Length > 0)
        {
            foreach (UIElement child in EditCanvas.Children)
            {
                var current = child.RenderTransform as TranslateTransform;
                var tx = current?.X ?? 0;
                var ty = current?.Y ?? 0;
                child.RenderTransform = new TranslateTransform(tx + shift.X, ty + shift.Y);
            }
            for (var i = 0; i < _mosaicRects.Count; i++)
            {
                var r = _mosaicRects[i];
                _mosaicRects[i] = new Rect(r.X + shift.X, r.Y + shift.Y, r.Width, r.Height);
            }
        }

        OverlayCanvas.Children.Clear();
        var w = OverlayCanvas.ActualWidth;
        var h = OverlayCanvas.ActualHeight;
        AddMask(new Rect(0, 0, w, rect.Y));
        AddMask(new Rect(0, rect.Bottom, w, Math.Max(0, h - rect.Bottom)));
        AddMask(new Rect(0, rect.Y, rect.X, rect.Height));
        AddMask(new Rect(rect.Right, rect.Y, Math.Max(0, w - rect.Right), rect.Height));

        EditFrame.Margin = new Thickness(rect.X, rect.Y, 0, 0);
        EditFrame.Width = rect.Width;
        EditFrame.Height = rect.Height;
        // 显式同步编辑画布尺寸（Canvas 尺寸不跟随 Border，需手动设置）。
        EditRoot.Width = rect.Width;
        EditRoot.Height = rect.Height;
        EditCanvas.Width = rect.Width;
        EditCanvas.Height = rect.Height;
        EditImage.Source = cropped;
        MosaicLayer.Source = null;
        if (_mosaicRects.Count > 0) ApplyMosaic();
        _editRect = rect;
        PlaceToolbar(rect);
        RenderHandles(rect);
    }

    private void EnterEditMode(Rect selDip, Int32Rect selPx)
    {
        _isEditing = true;
        OverlayCanvas.Children.Clear();
        // 保留选区外压暗蒙版。
        var w = OverlayCanvas.ActualWidth;
        var h = OverlayCanvas.ActualHeight;
        AddMask(new Rect(0, 0, w, selDip.Y));
        AddMask(new Rect(0, selDip.Bottom, w, Math.Max(0, h - selDip.Bottom)));
        AddMask(new Rect(0, selDip.Y, selDip.X, selDip.Height));
        AddMask(new Rect(selDip.Right, selDip.Y, Math.Max(0, w - selDip.Right), selDip.Height));

        var relative = new Int32Rect(
            selPx.X - _shot.VirtualBounds.X,
            selPx.Y - _shot.VirtualBounds.Y,
            selPx.Width,
            selPx.Height);
        var cropped = new CroppedBitmap(_shot.Image, relative);
        cropped.Freeze();

        EditFrame.Margin = new Thickness(selDip.X, selDip.Y, 0, 0);
        EditFrame.Width = selDip.Width;
        EditFrame.Height = selDip.Height;
        // 显式同步编辑画布尺寸：Canvas 默认宽度由内容决定，若不设置则选区扩大后
        // 标注仍被限制在原尺寸（画不进扩大后的区域）。
        EditRoot.Width = selDip.Width;
        EditRoot.Height = selDip.Height;
        EditCanvas.Width = selDip.Width;
        EditCanvas.Height = selDip.Height;
        EditImage.Source = cropped;
        MosaicLayer.Source = null;
        EditFrame.Visibility = Visibility.Visible;
        _editRect = selDip;
        EditCanvas.RenderTransform = null;
        PlaceToolbar(selDip);
        HintBar.Visibility = Visibility.Collapsed;
        _currentTool = EditTool.Arrow;
        HighlightTool(ArrowButton);
        RenderHandles(selDip); // 编辑模式保留手柄，可随时拖拽二次调整选区
        Focus();
    }

    // 工具条智能定位：优先选区左下角，放不下则左上角，再不行选区顶部内侧；水平方向夹在窗口内。
    private void PlaceToolbar(Rect selDip)
    {
        Toolbar.Visibility = Visibility.Visible;
        Toolbar.UpdateLayout();
        var width = Toolbar.ActualWidth;
        var height = Toolbar.ActualHeight;
        const double gap = 8;

        var x = selDip.X;
        var y = selDip.Bottom + gap;
        if (y + height > ActualHeight)
        {
            y = selDip.Y - height - gap; // 上方
            if (y < gap) y = selDip.Y + gap; // 顶部也放不下 → 选区顶部内侧
        }
        if (x + width > ActualWidth) x = Math.Max(gap, ActualWidth - width - gap);

        Toolbar.HorizontalAlignment = HorizontalAlignment.Left;
        Toolbar.VerticalAlignment = VerticalAlignment.Top;
        Toolbar.Margin = new Thickness(x, y, 0, 0);
    }

    // ---------- 编辑阶段 ----------

    private void OnEditMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool is not EditTool.Arrow and not EditTool.Rect and not EditTool.Mosaic) return;
        var pos = ClampEdit(e.GetPosition(EditCanvas));
        _start = pos;
        _current = pos;
        _isDrawing = true;
        CreateDraft();
        EditCanvas.CaptureMouse();
    }

    private void OnEditMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;
        _current = ClampEdit(e.GetPosition(EditCanvas));
        UpdateDraft();
    }

    private void OnEditMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        EditCanvas.ReleaseMouseCapture();
        _current = ClampEdit(e.GetPosition(EditCanvas));
        UpdateDraft();
        CommitDraft();
    }

    private Point ClampEdit(Point p) => new(
        Math.Clamp(p.X, 0, Math.Max(0, _editRect.Width)),
        Math.Clamp(p.Y, 0, Math.Max(0, _editRect.Height)));

    private void CreateDraft()
    {
        _draftElement = _currentTool switch
        {
            EditTool.Arrow => BuildArrow(_start, _current),
            EditTool.Rect => BuildRect(_start, _current),
            EditTool.Mosaic => BuildMosaicFrame(_start, _current),
            _ => null,
        };
        if (_draftElement is not null) EditCanvas.Children.Add(_draftElement);
    }

    private void UpdateDraft()
    {
        if (_draftElement is null) return;
        EditCanvas.Children.Remove(_draftElement);
        _draftElement = _currentTool switch
        {
            EditTool.Arrow => BuildArrow(_start, _current),
            EditTool.Rect => BuildRect(_start, _current),
            EditTool.Mosaic => BuildMosaicFrame(_start, _current),
            _ => null,
        };
        if (_draftElement is not null) EditCanvas.Children.Add(_draftElement);
    }

    private void CommitDraft()
    {
        if (_draftElement is null) return;
        var element = _draftElement;
        _draftElement = null;

        if (_currentTool == EditTool.Mosaic)
        {
            // 马赛克预览框绘制完成后移除，仅保留块化效果。
            EditCanvas.Children.Remove(element);
            var rect = RectFromPoints(_start, _current);
            if (rect.Width <= 1 || rect.Height <= 1) return;
            _mosaicRects.Add(rect);
            ApplyMosaic();
            _undoStack.Push(() =>
            {
                _mosaicRects.Remove(rect);
                ApplyMosaic();
            });
        }
        else
        {
            _annotations.Add(element);
            _undoStack.Push(() =>
            {
                EditCanvas.Children.Remove(element);
                _annotations.Remove(element);
            });
        }
    }

    private static Rect RectFromPoints(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    private UIElement BuildArrow(Point a, Point b)
    {
        var brush = new SolidColorBrush(CurrentColor);
        // 标准箭头：主线 + 实心三角形箭头头。
        var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
        const double headLength = 14;
        const double headWidth = 10;

        var line = new Line
        {
            X1 = a.X,
            Y1 = a.Y,
            X2 = b.X,
            Y2 = b.Y,
            Stroke = brush,
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

        var back = new Point(b.X - headLength * Math.Cos(angle), b.Y - headLength * Math.Sin(angle));
        var px = -Math.Sin(angle) * headWidth / 2;
        var py = Math.Cos(angle) * headWidth / 2;
        var p1 = new Point(back.X + px, back.Y + py);
        var p2 = new Point(back.X - px, back.Y - py);

        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = b, IsClosed = true };
        figure.Segments.Add(new LineSegment(p1, true));
        figure.Segments.Add(new LineSegment(p2, true));
        geometry.Figures.Add(figure);
        var head = new System.Windows.Shapes.Path { Data = geometry, Fill = brush };

        var container = new Grid();
        container.Children.Add(line);
        container.Children.Add(head);
        return container;
    }

    private UIElement BuildRect(Point a, Point b)
    {
        var rect = RectFromPoints(a, b);
        return new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Stroke = new SolidColorBrush(CurrentColor),
            StrokeThickness = 3,
            Fill = Brushes.Transparent,
        }
        .WithCanvasPos(rect);
    }

    private UIElement BuildMosaicFrame(Point a, Point b)
    {
        var rect = RectFromPoints(a, b);
        var frame = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0x9B, 0x59, 0xF6)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x9B, 0x59, 0xF6)),
        };
        Canvas.SetLeft(frame, rect.X);
        Canvas.SetTop(frame, rect.Y);
        return frame;
    }

    private void ApplyMosaic()
    {
        if (_mosaicRects.Count == 0)
        {
            MosaicLayer.Source = null;
            return;
        }
        if (EditImage.Source is not BitmapSource src) return;

        // 马赛克区域换算到物理像素，基于选区原图做像素块化。
        var rectsPx = _mosaicRects
            .Select(r => new Rect(r.X * _scale, r.Y * _scale, r.Width * _scale, r.Height * _scale))
            .ToList();
        MosaicLayer.Source = MosaicUtil.Apply(src, rectsPx, MosaicBlockPx);
    }

    private Color CurrentColor => Palette[_colorIndex % Palette.Length];

    private void UpdateColorButton()
    {
        ColorIndicator.Fill = new SolidColorBrush(CurrentColor);
    }

    private void HighlightTool(Button? active)
    {
        var buttons = new[] { ArrowButton, RectButton, MosaicButton };
        foreach (var b in buttons)
        {
            b.BorderBrush = ReferenceEquals(b, active) ? SelectionBorderBrush : Brushes.Transparent;
        }
    }

    private void ColorDot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag && int.TryParse(tag, out var index))
        {
            _colorIndex = index;
            UpdateColorButton();
        }
        ColorPalettePopup.IsOpen = false;
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag) return;
        switch (tag)
        {
            case "arrow":
                _currentTool = EditTool.Arrow;
                HighlightTool(ArrowButton);
                break;
            case "rect":
                _currentTool = EditTool.Rect;
                HighlightTool(RectButton);
                break;
            case "mosaic":
                _currentTool = EditTool.Mosaic;
                HighlightTool(MosaicButton);
                break;
            case "color":
                ColorPalettePopup.IsOpen = true;
                break;
            case "undo":
                UndoLast();
                break;
            case "save":
                SaveAs();
                break;
            case "done":
                Complete();
                break;
        }
    }

    private void UndoLast()
    {
        if (_undoStack.Count == 0) return;
        var undo = _undoStack.Pop();
        undo();
    }

    // ---------- 合成与输出 ----------

    private BitmapSource Compose()
    {
        // 输出尺寸 = 选区 DIP × 物理/DIP 比例（物理像素）。
        var widthPx = Math.Max(1, (int)Math.Round(_editRect.Width * _scale));
        var heightPx = Math.Max(1, (int)Math.Round(_editRect.Height * _scale));

        // 用 VisualBrush 把编辑工作区（DIP 布局）按目标尺寸精确拉伸渲染。
        // 不依赖 ScaleTransform：RenderTargetBitmap 在部分 DPI/分层窗口环境下
        // 不会把 ScaleTransform 作用到内容，导致内容按 96 DPI 原尺寸渲染、右侧/下侧留白。
        var brush = new VisualBrush(EditRoot) { Stretch = Stretch.Fill };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(brush, null, new Rect(0, 0, widthPx, heightPx));
        }

        var rtb = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private void Complete()
    {
        try
        {
            Clipboard.SetImage(Compose());
        }
        catch
        {
            // 剪贴板被占用等偶发失败不阻塞关闭。
        }
        Close();
    }

    private void SaveAs()
    {
        // 会话级记忆：默认图片文件夹；本次运行另存到其它目录后，后续优先用该目录。
        _saveDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var dialog = new SaveFileDialog
        {
            Title = "另存截图",
            Filter = "PNG 图片 (*.png)|*.png",
            FileName = $"LanFlow截图_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            InitialDirectory = _saveDirectory,
        };
        if (dialog.ShowDialog(this) != true) return;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Compose()));
        using var stream = System.IO.File.Create(dialog.FileName);
        encoder.Save(stream);
        _saveDirectory = System.IO.Path.GetDirectoryName(dialog.FileName);
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            UndoLast();
        }
        else if (e.Key == Key.Enter && _isEditing)
        {
            e.Handled = true;
            Complete();
        }
    }
}

internal static class ShapeExtensions
{
    public static T WithCanvasPos<T>(this T shape, Rect rect) where T : FrameworkElement
    {
        Canvas.SetLeft(shape, rect.X);
        Canvas.SetTop(shape, rect.Y);
        return shape;
    }
}

/// <summary>马赛克（像素块化）工具：对源图指定区域按块取均值色。</summary>
internal static class MosaicUtil
{
    public static BitmapSource Apply(BitmapSource source, IReadOnlyList<Rect> rects, int block)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        foreach (var r in rects)
        {
            var x0 = Math.Max(0, (int)r.X);
            var y0 = Math.Max(0, (int)r.Y);
            var x1 = Math.Min(width, (int)Math.Ceiling(r.Right));
            var y1 = Math.Min(height, (int)Math.Ceiling(r.Bottom));
            if (x1 <= x0 || y1 <= y0) continue;

            for (var by = y0; by < y1; by += block)
            {
                for (var bx = x0; bx < x1; bx += block)
                {
                    var bye = Math.Min(by + block, y1);
                    var bxe = Math.Min(bx + block, x1);
                    long bs = 0, gs = 0, rs = 0, asum = 0;
                    var count = 0;
                    for (var y = by; y < bye; y++)
                    {
                        for (var x = bx; x < bxe; x++)
                        {
                            var i = y * stride + x * 4;
                            bs += pixels[i];
                            gs += pixels[i + 1];
                            rs += pixels[i + 2];
                            asum += pixels[i + 3];
                            count++;
                        }
                    }
                    if (count == 0) continue;
                    var b = (byte)(bs / count);
                    var g = (byte)(gs / count);
                    var red = (byte)(rs / count);
                    var a = (byte)(asum / count);
                    for (var y = by; y < bye; y++)
                    {
                        for (var x = bx; x < bxe; x++)
                        {
                            var i = y * stride + x * 4;
                            pixels[i] = b;
                            pixels[i + 1] = g;
                            pixels[i + 2] = red;
                            pixels[i + 3] = a;
                        }
                    }
                }
            }
        }

        var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        wb.Freeze();
        return wb;
    }
}
