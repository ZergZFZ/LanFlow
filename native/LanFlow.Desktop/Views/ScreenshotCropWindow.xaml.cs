using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;
using LanFlow.Desktop.Services.Ocr;
using Microsoft.Win32;

namespace LanFlow.Desktop.Views;

/// <summary>
/// 全屏截图窗口。两阶段：
/// 选择阶段（拖动框选 + 放大镜）→ 标注阶段（7 种工具、撤销/重做、文字输入）。
/// 行为：Enter / Ctrl+C 合成（含标注）写剪贴板并关闭；Ctrl+S 另存 PNG；
/// P 生成置顶贴图窗；Esc（标注态）返回选择态并清空本次标注；Esc（选择态）取消退出。
/// 坐标约定：窗口内为 DIP，合成图按物理像素渲染。
/// </summary>
public partial class ScreenshotCropWindow : Window
{
    private enum Phase
    {
        Select,
        Annotate,
    }

    private static readonly Brush MaskBrush = new SolidColorBrush(Color.FromArgb(0xC0, 0x00, 0x00, 0x00));
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));
    private static readonly Brush HandleBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0xD4, 0x6B));
    private static readonly Brush DarkInkBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1A));
    private static readonly Brush LightInkBrush = new SolidColorBrush(Colors.White);

    private const double MinSelectionPixels = 4;
    private const int MosaicBlockPx = 16;
    private const double MagnifierZoom = 8;
    private const double MagnifierImageWidth = 144;
    private const double MagnifierImageHeight = 88;
    private const double MagnifierCardWidth = MagnifierImageWidth + 2;
    private const double MagnifierCardHeight = MagnifierImageHeight + 18 + 2;

    // 会话级记忆：另存为目录。
    private static string? _saveDirectory;

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

    private static readonly double[] StrokeWidths = [2, 4, 6, 8];

    private readonly ScreenShotResult _shot;
    private readonly List<AnnotationShape> _shapes = [];
    private readonly Stack<AnnotationShape> _redoStack = [];
    private double _scale = 1.0; // 物理像素 / DIP，由窗口实际尺寸实测

    private Phase _phase = Phase.Select;
    private bool _hasSelection;
    private Point _start;
    private Point _current;
    private bool _isDrawing;
    private string? _resizeHandle;  // n/s/e/w/nw/ne/se/sw
    private Point _resizeAnchorPos;
    private Rect _resizeAnchorRect;
    private Rect _editRect;

    private AnnotationTool _tool = AnnotationTool.Rect;
    private int _colorIndex;
    private int _widthIndex;
    private int _nextNumber = 1;
    private AnnotationShape? _draft;
    private TextBox? _textEditor;
    private Point _textAnchor;

    private readonly Settings _settings;
    private bool _ocrBusy;

    public ScreenshotCropWindow(ScreenShotResult shot, Settings settings)
    {
        InitializeComponent();
        _shot = shot;
        _settings = settings;
        FrozenImage.Source = shot.Image;
        AnnotationHost.Shapes = _shapes;

        MouseLeftButtonDown += OnRootMouseDown;
        MouseMove += OnRootMouseMove;
        MouseLeftButtonUp += OnRootMouseUp;
        PreviewMouseRightButtonDown += OnPreviewRightButtonDown;

        UpdateColorSwatch();
        SelectTool(AnnotationTool.Rect);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // 分层窗口（AllowsTransparency）在 RDP/高 DPI 下 WPF 的 DIP→物理换算会失效，
        // 直接用物理像素强制覆盖整个虚拟屏幕。
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

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
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

    protected override void OnClosed(EventArgs e)
    {
        // 大位图随窗口关闭立即断开引用；标注/历史一并清空。
        _textEditor = null;
        FrozenImage.Source = null;
        EditImage.Source = null;
        MosaicLayer.Source = null;
        MagnifierImage.Source = null;
        OverlayCanvas.Children.Clear();
        EditCanvas.Children.Clear();
        HandleCanvas.Children.Clear();
        _shapes.Clear();
        _redoStack.Clear();
        AnnotationHost.Shapes = Array.Empty<AnnotationShape>();
        AnnotationHost.Draft = null;
        _draft = null;

        base.OnClosed(e);

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        });
    }

    // ---------- 通用 ----------

    private Color CurrentColor => Palette[_colorIndex % Palette.Length];

    private double CurrentWidth => StrokeWidths[_widthIndex % StrokeWidths.Length];

    private Rect SelectionRect => new(
        Math.Min(_start.X, _current.X),
        Math.Min(_start.Y, _current.Y),
        Math.Abs(_current.X - _start.X),
        Math.Abs(_current.Y - _start.Y));

    private Point Clamp(Point p) => new(
        Math.Clamp(p.X, 0, Math.Max(1, ActualWidth)),
        Math.Clamp(p.Y, 0, Math.Max(1, ActualHeight)));

    private Point ToEditLocal(Point windowPoint) => new(
        Math.Clamp(windowPoint.X - _editRect.X, 0, Math.Max(0, _editRect.Width)),
        Math.Clamp(windowPoint.Y - _editRect.Y, 0, Math.Max(0, _editRect.Height)));

    private bool IsInsideSelection(Point p) =>
        p.X >= _editRect.X && p.X <= _editRect.Right && p.Y >= _editRect.Y && p.Y <= _editRect.Bottom;

    /// <summary>DIP 选区 → 物理像素矩形（绝对虚拟屏坐标），并夹紧到整图范围内。</summary>
    private Int32Rect ToPixelRect(Rect dipRect)
    {
        var virtualBounds = _shot.VirtualBounds;
        var relX = Math.Clamp(
            (int)Math.Round(dipRect.X * _scale),
            0,
            Math.Max(0, _shot.Image.PixelWidth - 1));
        var relY = Math.Clamp(
            (int)Math.Round(dipRect.Y * _scale),
            0,
            Math.Max(0, _shot.Image.PixelHeight - 1));
        var width = Math.Max(1, (int)Math.Round(dipRect.Width * _scale));
        var height = Math.Max(1, (int)Math.Round(dipRect.Height * _scale));
        width = Math.Min(width, Math.Max(1, _shot.Image.PixelWidth - relX));
        height = Math.Min(height, Math.Max(1, _shot.Image.PixelHeight - relY));

        return new Int32Rect(virtualBounds.X + relX, virtualBounds.Y + relY, width, height);
    }

    private static bool IsLight(Color c) =>
        (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 150;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    // ---------- 选择阶段 ----------

    private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_textEditor is not null && IsInsideEditor(e.OriginalSource)) return;
        if (_resizeHandle is not null) return;
        if (_phase == Phase.Annotate && ToolCard.IsMouseOver) return;

        Focus(); // 确保后续 1-7 / C / X / Enter 等快捷键回到窗口路由

        var pos = Clamp(e.GetPosition(RootGrid));

        if (_phase == Phase.Annotate && _hasSelection)
        {
            if (IsInsideSelection(pos))
            {
                BeginAnnotation(ToEditLocal(pos));
            }
            else
            {
                BeginNewSelection(pos);
            }
            return;
        }

        BeginNewSelection(pos);
    }

    private void OnRootMouseMove(object sender, MouseEventArgs e)
    {
        var pos = Clamp(e.GetPosition(RootGrid));

        if (_phase == Phase.Select)
        {
            UpdateMagnifier(pos);
        }

        if (_resizeHandle is not null)
        {
            ApplyResize(_resizeHandle, pos);
            if (_phase == Phase.Annotate)
            {
                RefreshEditFromSelection(reapplyMosaic: false);
            }
            else
            {
                RenderSelection();
            }
            return;
        }

        if (!_isDrawing) return;

        if (_phase == Phase.Select)
        {
            _current = pos;
            RenderSelection();
            return;
        }

        UpdateDraft(ToEditLocal(pos));
    }

    private void OnRootMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizeHandle is not null)
        {
            _resizeHandle = null;
            ReleaseMouseCapture();
            if (_phase == Phase.Annotate)
            {
                RefreshEditFromSelection();
            }
            else
            {
                RenderSelection();
            }
            return;
        }        if (!_isDrawing) return;
        _isDrawing = false;
        ReleaseMouseCapture();

        if (_phase == Phase.Select)
        {
            _current = Clamp(e.GetPosition(RootGrid));
            var rect = SelectionRect;
            if (rect.Width * _scale >= MinSelectionPixels && rect.Height * _scale >= MinSelectionPixels)
            {
                EnterAnnotate(rect);
            }
            else
            {
                _hasSelection = false;
                HintBar.Visibility = Visibility.Visible;
                RenderSelection();
            }
            return;
        }

        CommitDraft();
    }

    private void BeginNewSelection(Point pos)
    {
        if (_phase == Phase.Annotate)
        {
            ExitAnnotateToSelect();
        }

        _phase = Phase.Select;
        _hasSelection = false;
        _shapes.Clear();
        _redoStack.Clear();
        _nextNumber = 1;
        _draft = null;
        AnnotationHost.Draft = null;
        AnnotationHost.InvalidateVisual();
        _isDrawing = true;
        _start = pos;
        _current = pos;
        HintBar.Visibility = Visibility.Collapsed;
        RenderSelection();
        CaptureMouse();
    }

    private void RenderSelection()
    {
        OverlayCanvas.Children.Clear();
        if (_phase != Phase.Select) return;

        var rect = SelectionRect;
        if (rect.Width <= 0 && rect.Height <= 0) return;

        RenderMask(rect);

        var frame = new Rectangle
        {
            Fill = Brushes.Transparent,
            Stroke = AccentBrush,
            StrokeThickness = 2,
            Width = rect.Width,
            Height = rect.Height,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(frame, rect.X);
        Canvas.SetTop(frame, rect.Y);
        OverlayCanvas.Children.Add(frame);

        AddSizeBadge(rect);
    }

    private void RenderMask(Rect rect)
    {
        // 调用方已 Clear（RenderSelection/EnterAnnotate 共用入口），此处只追加。
        var w = Math.Max(0, ActualWidth);
        var h = Math.Max(0, ActualHeight);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        AddMask(new Rect(0, 0, w, Math.Max(0, rect.Y)));
        AddMask(new Rect(0, rect.Bottom, w, Math.Max(0, h - rect.Bottom)));
        AddMask(new Rect(0, rect.Y, Math.Max(0, rect.X), rect.Height));
        AddMask(new Rect(rect.Right, rect.Y, Math.Max(0, w - rect.Right), rect.Height));
    }

    private void AddMask(Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var mask = new Rectangle
        {
            Fill = MaskBrush,
            Width = rect.Width,
            Height = rect.Height,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(mask, rect.X);
        Canvas.SetTop(mask, rect.Y);
        OverlayCanvas.Children.Add(mask);
    }

    /// <summary>尺寸胶囊：圆角、强调色描边，显示物理像素尺寸。</summary>
    private void AddSizeBadge(Rect rect)
    {
        var px = (int)Math.Round(rect.Width * _scale);
        var py = (int)Math.Round(rect.Height * _scale);
        var badge = new Border
        {
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x14, 0x14, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x96, 0x4C, 0x9A, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 2, 7, 2),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"{px} × {py}",
                Foreground = Brushes.White,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
            },
        };
        badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = badge.DesiredSize;

        var x = rect.X;
        var y = rect.Y - size.Height - 6;
        if (y < 2) y = rect.Y + 4;
        if (x + size.Width > ActualWidth - 4) x = ActualWidth - size.Width - 4;
        if (x < 2) x = 2;

        Canvas.SetLeft(badge, x);
        Canvas.SetTop(badge, y);
        OverlayCanvas.Children.Add(badge);
    }

    private void EnterAnnotate(Rect selDip)
    {
        _phase = Phase.Annotate;
        _hasSelection = true;
        _start = selDip.TopLeft;
        _current = selDip.BottomRight;
        _shapes.Clear();
        _redoStack.Clear();
        _nextNumber = 1;
        _draft = null;
        AnnotationHost.Draft = null;

        HintBar.Visibility = Visibility.Collapsed;
        MagnifierLayer.Visibility = Visibility.Collapsed;
        EditFrame.Visibility = Visibility.Visible;
        ToolCard.Visibility = Visibility.Visible;

        UpdateEditFrame(selDip);
        _editRect = selDip;
        ApplyMosaic();
        RenderMask(selDip);
        RenderHandles(selDip);
        SelectTool(AnnotationTool.Rect);
        PlaceToolCard(selDip);
        AnnotationHost.InvalidateVisual();
        Focus();
    }

    /// <summary>按选区重建裁剪图 / 工作区尺寸 / 标注层尺寸。</summary>
    private void UpdateEditFrame(Rect selDip)
    {
        var px = ToPixelRect(selDip);
        var relative = new Int32Rect(
            px.X - _shot.VirtualBounds.X,
            px.Y - _shot.VirtualBounds.Y,
            px.Width,
            px.Height);
        var cropped = new CroppedBitmap(_shot.Image, relative);
        cropped.Freeze();

        EditImage.Source = cropped;
        MosaicLayer.Source = null;
        EditFrame.Margin = new Thickness(selDip.X, selDip.Y, 0, 0);
        EditFrame.Width = selDip.Width;
        EditFrame.Height = selDip.Height;
        EditRoot.Width = selDip.Width;
        EditRoot.Height = selDip.Height;
        AnnotationHost.Width = selDip.Width;
        AnnotationHost.Height = selDip.Height;
        EditCanvas.Width = selDip.Width;
        EditCanvas.Height = selDip.Height;
        AnnotationHost.InvalidateVisual();
    }

    /// <summary>标注阶段调整选区后重建工作区；标注相对原图锚定，按原点差值反向平移。
    /// 拖拽进行中（reapplyMosaic=false）跳过马赛克全量重算，MouseUp 后统一重算（P2-2）。</summary>
    private void RefreshEditFromSelection(bool reapplyMosaic = true)
    {
        var rect = SelectionRect;
        if (rect.Width * _scale < MinSelectionPixels || rect.Height * _scale < MinSelectionPixels) return;

        var delta = new Vector(rect.X - _editRect.X, rect.Y - _editRect.Y);
        if (delta.Length > 0)
        {
            var shift = new Vector(-delta.X, -delta.Y);
            foreach (var shape in _shapes)
            {
                shape.Offset(shift);
            }
        }

        UpdateEditFrame(rect);
        _editRect = rect;
        if (reapplyMosaic)
        {
            ApplyMosaic();
        }
        RenderMask(rect);
        RenderHandles(rect);
        PlaceToolCard(rect);
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

    /// <summary>强调色选区框 + 8 个手柄，置于工作区之上。</summary>
    private void RenderHandles(Rect rect)
    {
        HandleCanvas.Children.Clear();

        var frame = new Rectangle
        {
            Fill = Brushes.Transparent,
            Stroke = AccentBrush,
            StrokeThickness = 2,
            Width = rect.Width,
            Height = rect.Height,
            IsHitTestVisible = false,
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
                Cursor = Cursors.Hand,
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
        _resizeHandle = dir;
        _resizeAnchorPos = Clamp(e.GetPosition(RootGrid));
        _resizeAnchorRect = _phase == Phase.Annotate ? _editRect : SelectionRect;
        e.Handled = true;
        CaptureMouse();
    }

    // ---------- 标注阶段 ----------

    private void BeginAnnotation(Point local)
    {
        var color = CurrentColor;
        var width = CurrentWidth;

        switch (_tool)
        {
            case AnnotationTool.Number:
                AddShape(new AnnotationShape
                {
                    Tool = AnnotationTool.Number,
                    A = local,
                    B = local,
                    Text = _nextNumber.ToString(CultureInfo.InvariantCulture),
                    Color = color,
                    StrokeWidth = width,
                });
                return;

            case AnnotationTool.Text:
                BeginText(local);
                return;

            default:
                _draft = new AnnotationShape
                {
                    Tool = _tool,
                    A = local,
                    B = local,
                    Color = color,
                    StrokeWidth = width,
                };
                if (_tool == AnnotationTool.Pen)
                {
                    _draft.Points.Add(local);
                }
                AnnotationHost.Draft = _draft;
                AnnotationHost.InvalidateVisual();
                _isDrawing = true;
                CaptureMouse();
                return;
        }
    }

    private void UpdateDraft(Point local)
    {
        if (_draft is null) return;
        _draft.B = local;
        if (_draft.Tool == AnnotationTool.Pen && _draft.Points.Count > 0)
        {
            var last = _draft.Points[^1];
            if ((local - last).Length >= 1.5)
            {
                _draft.Points.Add(local);
            }
        }
        AnnotationHost.InvalidateVisual();
    }

    private void CommitDraft()
    {
        var draft = _draft;
        _draft = null;
        AnnotationHost.Draft = null;
        if (draft is null) return;

        var degenerate = draft.Tool == AnnotationTool.Pen
            ? draft.Points.Count < 2
            : Math.Abs(draft.B.X - draft.A.X) < 4 && Math.Abs(draft.B.Y - draft.A.Y) < 4;

        if (degenerate)
        {
            AnnotationHost.InvalidateVisual();
            return;
        }

        AddShape(draft);
    }

    private void AddShape(AnnotationShape shape)
    {
        _shapes.Add(shape);
        _redoStack.Clear();
        if (shape.Tool == AnnotationTool.Number)
        {
            _nextNumber++;
        }
        if (shape.Tool == AnnotationTool.Mosaic)
        {
            ApplyMosaic();
        }
        AnnotationHost.InvalidateVisual();
    }

    private void UndoShape()
    {
        if (_shapes.Count == 0) return;
        var shape = _shapes[^1];
        _shapes.RemoveAt(_shapes.Count - 1);
        if (shape.Tool == AnnotationTool.Number && _nextNumber > 1)
        {
            _nextNumber--;
        }
        _redoStack.Push(shape);
        AfterHistoryChange(shape);
    }

    private void RedoShape()
    {
        if (_redoStack.Count == 0) return;
        var shape = _redoStack.Pop();
        _shapes.Add(shape);
        if (shape.Tool == AnnotationTool.Number)
        {
            _nextNumber++;
        }
        AfterHistoryChange(shape);
    }

    private void AfterHistoryChange(AnnotationShape changed)
    {
        if (changed.Tool == AnnotationTool.Mosaic)
        {
            ApplyMosaic();
        }
        AnnotationHost.InvalidateVisual();
    }

    private void ApplyMosaic()
    {
        if (EditImage.Source is not BitmapSource source)
        {
            MosaicLayer.Source = null;
            return;
        }

        var rects = new List<Rect>();
        foreach (var shape in _shapes)
        {
            if (shape.Tool != AnnotationTool.Mosaic) continue;
            var bounds = shape.Bounds;
            rects.Add(new Rect(
                bounds.X * _scale,
                bounds.Y * _scale,
                bounds.Width * _scale,
                bounds.Height * _scale));
        }

        MosaicLayer.Source = rects.Count == 0 ? null : MosaicUtil.Apply(source, rects, MosaicBlockPx);
    }

    private void SelectTool(AnnotationTool tool)
    {
        _tool = tool;
        var entries = new (Button Button, AnnotationTool Tool)[]
        {
            (RectButton, AnnotationTool.Rect),
            (ArrowButton, AnnotationTool.Arrow),
            (PenButton, AnnotationTool.Pen),
            (MosaicButton, AnnotationTool.Mosaic),
            (NumberButton, AnnotationTool.Number),
            (TextButton, AnnotationTool.Text),
            (EllipseButton, AnnotationTool.Ellipse),
        };
        var activeBrush = new SolidColorBrush(CurrentColor);
        var ink = IsLight(CurrentColor) ? DarkInkBrush : LightInkBrush;
        foreach (var (button, entryTool) in entries)
        {
            var active = entryTool == tool;
            button.Background = active ? activeBrush : Brushes.Transparent;
            button.Foreground = active ? ink : LightInkBrush;
        }
    }

    private void CycleColor()
    {
        _colorIndex = (_colorIndex + 1) % Palette.Length;
        UpdateColorSwatch();
        SelectTool(_tool);
    }

    private void CycleWidth()
    {
        _widthIndex = (_widthIndex + 1) % StrokeWidths.Length;
    }

    private void UpdateColorSwatch()
    {
        ColorSwatch.Fill = new SolidColorBrush(CurrentColor);
    }

    private void PlaceToolCard(Rect selDip)
    {
        ToolCard.Visibility = Visibility.Visible;
        ToolLayer.UpdateLayout();
        ToolCard.UpdateLayout();

        var width = ToolCard.ActualWidth > 0 ? ToolCard.ActualWidth : 458;
        var height = ToolCard.ActualHeight > 0 ? ToolCard.ActualHeight : 82;
        var gap = 10;

        var x = selDip.X;
        var y = selDip.Bottom + gap;
        if (y + height > ActualHeight - 6)
        {
            y = selDip.Y - height - gap;
        }
        if (y < 6) y = 6;
        if (x + width > ActualWidth - 6) x = ActualWidth - width - 6;
        if (x < 6) x = 6;

        Canvas.SetLeft(ToolCard, x);
        Canvas.SetTop(ToolCard, y);
    }

    private void ExitAnnotateToSelect()
    {
        CancelText();
        _phase = Phase.Select;
        _hasSelection = false;
        _shapes.Clear();
        _redoStack.Clear();
        _nextNumber = 1;
        _draft = null;
        _isDrawing = false;
        _resizeHandle = null;
        AnnotationHost.Draft = null;
        AnnotationHost.Shapes = _shapes;
        AnnotationHost.InvalidateVisual();

        EditFrame.Visibility = Visibility.Collapsed;
        ToolCard.Visibility = Visibility.Collapsed;
        HandleCanvas.Children.Clear();
        OverlayCanvas.Children.Clear();
        EditImage.Source = null;
        MosaicLayer.Source = null;
        EditCanvas.Children.Clear();
        HintBar.Visibility = Visibility.Visible;
        _editRect = Rect.Empty;
        _start = _current = new Point(0, 0);
        Focus();
    }

    private void ToolCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 卡片留白/提示行不是“点空白处重新框选”的入口。
        e.Handled = true;
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var index)) return;

        if (index is >= 0 and <= 6)
        {
            SelectTool((AnnotationTool)index);
        }
        else if (index == 7)
        {
            CycleColor();
        }
        else if (index == 8)
        {
            UndoShape();
        }
        else if (index == 9)
        {
            _ = RunOcrAsync();
        }
        e.Handled = true;
    }

    // ---------- 文字输入 ----------

    private void BeginText(Point local)
    {
        CommitText();

        var box = new TextBox
        {
            Width = 240,
            Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)),
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x4C, 0x9A, 0xFF)),
            BorderThickness = new Thickness(1),
            FontSize = 17,
            Padding = new Thickness(4, 2, 4, 2),
            AcceptsReturn = false,
        };

        var x = Math.Min(local.X, Math.Max(0, _editRect.Width - box.Width - 4));
        var y = Math.Min(local.Y, Math.Max(0, _editRect.Height - 36));
        _textAnchor = new Point(x, y);
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);

        box.PreviewKeyDown += TextEditor_PreviewKeyDown;
        box.LostKeyboardFocus += TextEditor_LostKeyboardFocus;

        EditCanvas.Children.Add(box);
        _textEditor = box;

        // 从 MouseDown 直接 Focus 会与随后的 MouseUp 抢焦点，延后一拍。
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_textEditor == box)
            {
                box.Focus();
                Keyboard.Focus(box);
            }
        });
    }

    private bool IsInsideEditor(object? source)
    {
        if (_textEditor is null || source is not DependencyObject dependencyObject) return false;
        var current = dependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, _textEditor)) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void TextEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitText();
            Focus();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelText();
            Focus();
        }
    }

    private void TextEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitText();
    }

    private void CommitText()
    {
        var box = _textEditor;
        if (box is null) return;
        _textEditor = null;

        var text = box.Text;
        box.PreviewKeyDown -= TextEditor_PreviewKeyDown;
        box.LostKeyboardFocus -= TextEditor_LostKeyboardFocus;
        EditCanvas.Children.Remove(box);

        if (!string.IsNullOrWhiteSpace(text))
        {
            AddShape(new AnnotationShape
            {
                Tool = AnnotationTool.Text,
                A = _textAnchor,
                B = _textAnchor,
                Text = text.Trim(),
                Color = CurrentColor,
                StrokeWidth = CurrentWidth,
            });
        }
    }

    private void CancelText()
    {
        var box = _textEditor;
        if (box is null) return;
        _textEditor = null;
        box.PreviewKeyDown -= TextEditor_PreviewKeyDown;
        box.LostKeyboardFocus -= TextEditor_LostKeyboardFocus;
        EditCanvas.Children.Remove(box);
    }

    // ---------- 放大镜 ----------

    private void UpdateMagnifier(Point dip)
    {
        if (_phase != Phase.Select || FrozenImage.Source is not BitmapSource source)
        {
            MagnifierLayer.Visibility = Visibility.Collapsed;
            return;
        }

        var px = (int)Math.Round(dip.X * _scale);
        var py = (int)Math.Round(dip.Y * _scale);
        px = Math.Clamp(px, 0, Math.Max(0, source.PixelWidth - 1));
        py = Math.Clamp(py, 0, Math.Max(0, source.PixelHeight - 1));

        var srcW = Math.Min((int)(MagnifierImageWidth / MagnifierZoom), source.PixelWidth);
        var srcH = Math.Min((int)(MagnifierImageHeight / MagnifierZoom), source.PixelHeight);
        var sx = Math.Clamp(px - srcW / 2, 0, Math.Max(0, source.PixelWidth - srcW));
        var sy = Math.Clamp(py - srcH / 2, 0, Math.Max(0, source.PixelHeight - srcH));

        try
        {
            var crop = new CroppedBitmap(source, new Int32Rect(sx, sy, srcW, srcH));
            crop.Freeze();
            MagnifierImage.Source = crop;

            var one = new CroppedBitmap(source, new Int32Rect(px, py, 1, 1));
            var fmt = new FormatConvertedBitmap(one, PixelFormats.Bgra32, null, 0);
            var buffer = new byte[4];
            fmt.CopyPixels(buffer, 4, 0);
            MagnifierText.Text = $"{px}, {py}   #{buffer[2]:X2}{buffer[1]:X2}{buffer[0]:X2}";
        }
        catch
        {
            MagnifierText.Text = $"{px}, {py}";
        }

        const double offset = 24;
        var x = dip.X + offset;
        var y = dip.Y + offset;
        if (x + MagnifierCardWidth > ActualWidth - 4) x = dip.X - offset - MagnifierCardWidth;
        if (y + MagnifierCardHeight > ActualHeight - 4) y = dip.Y - offset - MagnifierCardHeight;
        x = Math.Max(4, x);
        y = Math.Max(4, y);

        Canvas.SetLeft(MagnifierCard, x);
        Canvas.SetTop(MagnifierCard, y);
        MagnifierLayer.Visibility = Visibility.Visible;
    }

    // ---------- 合成与输出 ----------

    /// <summary>
    /// 模型化合成：按物理尺寸绘制 裁剪图 → 马赛克层 → 标注矢量。
    /// 与屏幕预览共用同一套 AnnotationShape，保证“预览即输出”。
    /// </summary>
    private BitmapSource Compose()
    {
        var px = ToPixelRect(_editRect);
        var widthPx = px.Width;
        var heightPx = px.Height;
        var scaleX = _editRect.Width > 0 ? widthPx / _editRect.Width : _scale;
        var scaleY = _editRect.Height > 0 ? heightPx / _editRect.Height : _scale;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scaleX, scaleY));
            var rect = new Rect(0, 0, _editRect.Width, _editRect.Height);
            if (EditImage.Source is ImageSource crop)
            {
                dc.DrawImage(crop, rect);
            }
            if (MosaicLayer.Source is ImageSource mosaic)
            {
                dc.DrawImage(mosaic, rect);
            }
            foreach (var shape in _shapes)
            {
                AnnotationRenderer.Draw(dc, shape, 1.0);
            }
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private void CopyAndClose()
    {
        BitmapSource image;
        try
        {
            image = Compose();
        }
        catch
        {
            Close();
            return;
        }

        // 与文本复制同构：Win32 CF_DIB 优先，OLE 回退 + 重试（1.6.1 同类问题）。
        ClipboardImage.TrySet(image);
        Close();
    }

    private void SaveAsAndClose()
    {
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

    private void PinAndClose()
    {
        var image = Compose();
        var pixelRect = ToPixelRect(_editRect);
        var pin = new PinWindow(image, pixelRect);
        pin.Show();
        Close();
    }

    // ---------- 取字 ----------

    private async Task RunOcrAsync()
    {
        if (_ocrBusy || _phase != Phase.Annotate || !_hasSelection) return;

        if (!_settings.OcrEnabled)
        {
            ShowToast("取字未启用：请在 设置 → 扩展与 OCR 开启");
            return;
        }

        var location = OcrBundleLocator.Locate();
        if (location.Status != OcrBundleStatus.Installed)
        {
            var setup = new OcrSetupWindow { Owner = this };
            if (setup.ShowDialog() != true) return;
        }

        _ocrBusy = true;
        OcrButton.IsEnabled = false;
        var hint = ToolHint.Text;

        // 分阶段提示：首次取字需要加载 ONNX Runtime 与 det/rec 模型（1–3s），
        // 与推理阶段文案区分，避免"卡死"错觉（P2-4）。
        void OnEnginePhase(bool initializing)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!_ocrBusy) return;
                ToolHint.Text = initializing ? "正在加载识别模型（首次约数秒）…" : "正在识别选区文字…";
            });
        }

        OcrEngineHost.Instance.PhaseChanged += OnEnginePhase;
        ToolHint.Text = OcrEngineHost.Instance.IsLoaded ? "正在识别选区文字…" : "正在准备取字…";

        try
        {
            var image = Compose();
            var result = await OcrEngineHost.Instance.RecognizeAsync(image);
            var text = OcrTextNormalizer.Normalize(result.FullText);

            if (text.Length == 0)
            {
                ShowToast("未识别到文字");
                return;
            }

            if (_settings.OcrShowResultWindow)
            {
                var window = new OcrResultWindow(text) { Owner = this };
                window.ShowDialog();
                if (window.Copied) Close();
                return;
            }

            if (!TryCopyText(text))
            {
                ShowToast("复制失败，请重试");
                return;
            }

            ShowToast($"已复制 {text.Length} 字");
            Close();
        }
        catch (Exception ex)
        {
            ShowToast("取字失败：" + ex.Message);
        }
        finally
        {
            OcrEngineHost.Instance.PhaseChanged -= OnEnginePhase;
            _ocrBusy = false;
            ToolHint.Text = hint;
            OcrButton.IsEnabled = true;
        }
    }

    private static bool TryCopyText(string text) => ClipboardText.TrySet(text);

    private void ShowToast(string message)
    {
        var px = ToPixelRect(_editRect);
        var anchor = new Int32Rect(
            _shot.VirtualBounds.X + px.X,
            _shot.VirtualBounds.Y + px.Y,
            px.Width,
            px.Height);
        new ToastWindow(message, anchor).Show();
    }

    // ---------- 取消语义（Esc / 右键共用） ----------

    /// <summary>
    /// 统一取消手势，与 Esc 在各阶段的行为完全一致：
    /// 文字输入中=取消文字；标注态=返回框选态；选择态=关闭截图。
    /// </summary>
    private void HandleCancelGesture(bool cancelTextInput)
    {
        // 绘制/拖拽进行中：先终止当前手势，再按阶段决策。
        if (_isDrawing || _resizeHandle is not null)
        {
            _isDrawing = false;
            _resizeHandle = null;
            _draft = null;
            AnnotationHost.Draft = null;
            ReleaseMouseCapture();
            AnnotationHost.InvalidateVisual();
        }

        var action = ScreenshotCancelPolicy.Decide(
            hasTextEditor: _textEditor is not null,
            isAnnotating: _phase == Phase.Annotate);

        switch (action)
        {
            case CancelAction.CancelText:
                if (cancelTextInput)
                {
                    CancelText();
                    Focus();
                }
                return;
            case CancelAction.ExitAnnotate:
                ExitAnnotateToSelect();
                return;
            default:
                Close();
                return;
        }
    }

    /// <summary>
    /// 右键=取消（等同 Esc）：选择态关窗、标注态返回框选、文字输入中取消文字。
    /// 全窗接管，避免叠加系统文本菜单而分散交互。
    /// </summary>
    private void OnPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        HandleCancelGesture(cancelTextInput: true);
    }

    // ---------- 键盘 ----------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 文字框打开时键盘归它所有（Enter 提交文字、Esc 取消文字，而不是整个截图）。
        if (_textEditor is not null) return;

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HandleCancelGesture(cancelTextInput: false);
            return;
        }

        if (_phase != Phase.Annotate || !_hasSelection) return;

        if (ctrl && e.Key == Key.C)
        {
            e.Handled = true;
            CopyAndClose();
            return;
        }
        if (ctrl && e.Key == Key.S)
        {
            e.Handled = true;
            SaveAsAndClose();
            return;
        }
        if (ctrl && e.Key == Key.Z)
        {
            e.Handled = true;
            UndoShape();
            return;
        }
        if (ctrl && e.Key == Key.Y)
        {
            e.Handled = true;
            RedoShape();
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                CopyAndClose();
                return;
            case Key.P:
                e.Handled = true;
                PinAndClose();
                return;
            case Key.T:
                e.Handled = true;
                _ = RunOcrAsync();
                return;
            case Key.C:
                e.Handled = true;
                CycleColor();
                return;
            case Key.X:
                e.Handled = true;
                CycleWidth();
                return;
            case Key.D1:
            case Key.NumPad1:
                e.Handled = true;
                SelectTool(AnnotationTool.Rect);
                return;
            case Key.D2:
            case Key.NumPad2:
                e.Handled = true;
                SelectTool(AnnotationTool.Arrow);
                return;
            case Key.D3:
            case Key.NumPad3:
                e.Handled = true;
                SelectTool(AnnotationTool.Pen);
                return;
            case Key.D4:
            case Key.NumPad4:
                e.Handled = true;
                SelectTool(AnnotationTool.Mosaic);
                return;
            case Key.D5:
            case Key.NumPad5:
                e.Handled = true;
                SelectTool(AnnotationTool.Number);
                return;
            case Key.D6:
            case Key.NumPad6:
                e.Handled = true;
                SelectTool(AnnotationTool.Text);
                return;
            case Key.D7:
            case Key.NumPad7:
                e.Handled = true;
                SelectTool(AnnotationTool.Ellipse);
                return;
        }
    }
}
