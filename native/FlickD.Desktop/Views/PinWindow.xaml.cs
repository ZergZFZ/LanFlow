using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FlickD.Desktop.Services;
using Microsoft.Win32;

namespace FlickD.Desktop.Views;

/// <summary>
/// 置顶贴图窗：无边框、按物理像素定位。
/// 左键拖动移动、滚轮与右下角手柄等比缩放（10%–400%）、双击/Esc 关闭、右键菜单操作。
/// </summary>
public partial class PinWindow : Window
{
    private const int GripPx = 14;
    private const int SwpNoActivate = 0x0010;
    private const int SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private BitmapSource? _image;
    private IntPtr _hwnd;
    private Int32Rect _placement;
    private int _widthPx;
    private int _heightPx;

    private bool _sizing;
    private RECT _sizeFrom;
    private POINT _sizeCursorFrom;

    private readonly DispatcherTimer _pillTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    public PinWindow(BitmapSource image, Int32Rect physicalPlacement)
    {
        InitializeComponent();
        _image = image;
        _placement = physicalPlacement;
        _widthPx = physicalPlacement.Width;
        _heightPx = physicalPlacement.Height;

        PinImage.Source = image;
        _pillTimer.Tick += (_, _) =>
        {
            _pillTimer.Stop();
            SizePill.Visibility = Visibility.Collapsed;
        };

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseWheel += OnMouseWheel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Place(_placement.X, _placement.Y, _placement.Width, _placement.Height, showPill: false);
        Opacity = 1;
        Activate();
        Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _pillTimer.Stop();
        PinImage.Source = null;
        _image = null;
        base.OnClosed(e);
    }

    // ---------- 尺寸与位置 ----------

    private void Place(int x, int y, int width, int height, bool showPill)
    {
        if (_hwnd == IntPtr.Zero) return;
        SetWindowPos(_hwnd, HwndTopmost, x, y, width, height, SwpNoActivate | SwpShowWindow);
        _widthPx = width;
        _heightPx = height;

        if (_image is not null)
        {
            var upscaled = width >= _image.PixelWidth;
            RenderOptions.SetBitmapScalingMode(
                PinImage,
                upscaled ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        }

        if (showPill)
        {
            var percent = _image is null || _image.PixelWidth <= 0
                ? 100
                : (int)Math.Round(100.0 * width / _image.PixelWidth);
            ShowPill($"{width} x {height}   {percent}%");
        }
    }

    private RECT GetCurrentRect()
    {
        if (_hwnd != IntPtr.Zero && GetWindowRect(_hwnd, out var rect))
        {
            return rect;
        }
        return new RECT { Left = _placement.X, Top = _placement.Y, Right = _placement.X + _widthPx, Bottom = _placement.Y + _heightPx };
    }

    private void ApplyWidth(int width, bool keepCenter)
    {
        if (_image is null || _image.PixelWidth <= 0 || _image.PixelHeight <= 0) return;

        var minWidth = Math.Max(48, _image.PixelWidth / 10);
        var maxWidth = Math.Max(minWidth, _image.PixelWidth * 4);
        width = Math.Clamp(width, minWidth, maxWidth);
        var height = (int)Math.Round((double)width * _image.PixelHeight / _image.PixelWidth);
        if (height < 24) height = 24;

        var current = GetCurrentRect();
        var x = current.Left;
        var y = current.Top;
        if (keepCenter)
        {
            x = current.Left + (current.Width - width) / 2;
            y = current.Top + (current.Height - height) / 2;
        }

        Place(x, y, width, height, showPill: true);
    }

    private void ShowPill(string text)
    {
        SizePillText.Text = text;
        SizePill.Visibility = Visibility.Visible;
        _pillTimer.Stop();
        _pillTimer.Start();
    }

    private bool InGrip(Point p) =>
        p.X >= ActualWidth - GripPx && p.Y >= ActualHeight - GripPx;

    // ---------- 鼠标 ----------

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            e.Handled = true;
            Close();
            return;
        }

        var pos = e.GetPosition(Root);
        if (InGrip(pos))
        {
            _sizing = true;
            _sizeFrom = GetCurrentRect();
            GetCursorPos(out _sizeCursorFrom);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // 拖动过程中窗口被关闭等边界情况，忽略。
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_sizing)
        {
            GetCursorPos(out var cursor);
            ApplyWidth(_sizeFrom.Width + (cursor.X - _sizeCursorFrom.X), keepCenter: false);
            return;
        }

        Cursor = InGrip(e.GetPosition(Root)) ? Cursors.SizeNWSE : Cursors.SizeAll;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_sizing) return;
        _sizing = false;
        ReleaseMouseCapture();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        var width = (int)Math.Round(_widthPx * factor);
        if (width == _widthPx) width += e.Delta > 0 ? 1 : -1;
        ApplyWidth(width, keepCenter: true);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    // ---------- 右键菜单 ----------

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_image is null) return;
        ShowPill(ClipboardImage.TrySet(_image) ? "已复制" : "复制失败");
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (_image is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "另存贴图",
            Filter = "PNG 图片 (*.png)|*.png",
            FileName = $"FlickD贴图_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_image));
            using var stream = System.IO.File.Create(dialog.FileName);
            encoder.Save(stream);
            ShowPill("已保存");
        }
        catch
        {
            ShowPill("保存失败");
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ApplyWidth((int)Math.Round(_widthPx * 1.25), keepCenter: true);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ApplyWidth((int)Math.Round(_widthPx * 0.8), keepCenter: true);

    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        if (_image is null) return;
        ApplyWidth(_image.PixelWidth, keepCenter: true);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- Win32 ----------

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
