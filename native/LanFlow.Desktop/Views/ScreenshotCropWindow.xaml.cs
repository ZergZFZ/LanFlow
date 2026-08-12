using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop.Views;

/// <summary>
/// 全屏截图框选窗口：显示冻结截图，拖拽绘制选区，松开自动复制选区并关闭。
/// 坐标约定：窗口内坐标为 DIP，确认时按 _scale 换算为物理像素（相对虚拟屏幕）。
/// </summary>
public partial class ScreenshotCropWindow : Window
{
    private static readonly Brush MaskBrush = new SolidColorBrush(Color.FromArgb(0x90, 0x00, 0x00, 0x00));
    private static readonly Brush SelectionBorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0x9A, 0xFF));
    private const double MinSelectionPixels = 4;

    private readonly ScreenShotResult _shot;
    private readonly double _scale;
    private Point _start;
    private Point _current;
    private bool _isDrawing;

    public ScreenshotCropWindow(ScreenShotResult shot)
    {
        InitializeComponent();
        _shot = shot;

        // 统一 DPI 近似：物理虚拟宽 ÷ DIP 虚拟宽。100% 缩放 = 1.0，125% = 1.25（多屏混合 DPI 有偏差，见设计文档风险）。
        var dipWidth = SystemParameters.VirtualScreenWidth;
        _scale = dipWidth > 0 ? shot.VirtualBounds.Width / dipWidth : 1.0;

        Left = shot.VirtualBounds.X / _scale;
        Top = shot.VirtualBounds.Y / _scale;
        Width = shot.VirtualBounds.Width / _scale;
        Height = shot.VirtualBounds.Height / _scale;

        FrozenImage.Source = shot.Image;
        FrozenImage.Width = shot.Image.PixelWidth / _scale;
        FrozenImage.Height = shot.Image.PixelHeight / _scale;
        OverlayCanvas.Width = FrozenImage.Width;
        OverlayCanvas.Height = FrozenImage.Height;

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Activate();
        Focus();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var pos = Clamp(e.GetPosition(OverlayCanvas));
        _isDrawing = true;
        _start = pos;
        _current = pos;
        RenderSelection();
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;
        _current = Clamp(e.GetPosition(OverlayCanvas));
        RenderSelection();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        ReleaseMouseCapture();
        _current = Clamp(e.GetPosition(OverlayCanvas));
        RenderSelection();
        ConfirmSelection();
    }

    private Point Clamp(Point p) => new(
        Math.Clamp(p.X, 0, OverlayCanvas.Width),
        Math.Clamp(p.Y, 0, OverlayCanvas.Height));

    private Rect SelectionRect => new(
        Math.Min(_start.X, _current.X),
        Math.Min(_start.Y, _current.Y),
        Math.Abs(_current.X - _start.X),
        Math.Abs(_current.Y - _start.Y));

    private void RenderSelection()
    {
        OverlayCanvas.Children.Clear();
        var rect = SelectionRect;
        var w = OverlayCanvas.Width;
        var h = OverlayCanvas.Height;

        if (rect.Width > 0 || rect.Height > 0)
        {
            // 选区外四块压暗蒙版。
            AddMask(new Rect(0, 0, w, rect.Y));
            AddMask(new Rect(0, rect.Bottom, w, Math.Max(0, h - rect.Bottom)));
            AddMask(new Rect(0, rect.Y, rect.X, rect.Height));
            AddMask(new Rect(rect.Right, rect.Y, Math.Max(0, w - rect.Right), rect.Height));

            // 选区边框。
            var frame = new Rectangle
            {
                Fill = Brushes.Transparent,
                Stroke = SelectionBorderBrush,
                StrokeThickness = 1,
                Width = rect.Width,
                Height = rect.Height,
            };
            Canvas.SetLeft(frame, rect.X);
            Canvas.SetTop(frame, rect.Y);
            OverlayCanvas.Children.Add(frame);

            // 物理像素尺寸文本。
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
        }
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

        var pixelRect = new Int32Rect(
            (int)Math.Round(_shot.VirtualBounds.X + rect.X * _scale),
            (int)Math.Round(_shot.VirtualBounds.Y + rect.Y * _scale),
            (int)Math.Round(rect.Width * _scale),
            (int)Math.Round(rect.Height * _scale));

        if (ScreenshotService.CopySelection(_shot, pixelRect))
        {
            Close();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
