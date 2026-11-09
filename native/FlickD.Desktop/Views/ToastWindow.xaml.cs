using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FlickD.Desktop.Views;

/// <summary>
/// 轻量提示条：不抢焦点、置顶、1.4 秒后淡出自动关闭。
/// 定位锚点为物理像素选区矩形，越界时回弹到屏幕内。
/// </summary>
public partial class ToastWindow : Window
{
    private const int SwpNoActivate = 0x0010;
    private const int SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly DispatcherTimer _hold = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    private readonly Int32Rect _anchor;
    private IntPtr _hwnd;
    private bool _loaded;
    private bool _initialized;
    private bool _placed;

    public ToastWindow(string text, Int32Rect physicalAnchor)
    {
        InitializeComponent();
        _anchor = physicalAnchor;
        PillText.Text = text;
        _hold.Interval = ComputeHoldDuration(text);
        Loaded += (_, _) =>
        {
            _loaded = true;
            TryStart();
        };
    }

    /// <summary>按文本长度自适应停留时长（P2-5）：长提示（如开关位置指引）需要更长的阅读时间。
    /// internal 供测试断言策略边界。</summary>
    internal static TimeSpan ComputeHoldDuration(string text)
    {
        var extra = Math.Min(1600, Math.Max(0, text.Length - 12) * 60);
        return TimeSpan.FromMilliseconds(1400 + extra);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Opacity = 0;
        _initialized = true;
        TryStart();
    }

    /// <summary>
    /// SizeToContent 场景下 WPF 可能先触发 Loaded 再触发 SourceInitialized，
    /// 因此必须等句柄与布局都就绪后再定位，否则窗口会停在默认级联位置且保持透明。
    /// </summary>
    private void TryStart()
    {
        if (_placed || !_loaded || !_initialized) return;
        _placed = true;

        Place();
        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            FadeOut();
        };
        _hold.Start();
    }

    private void Place()
    {
        if (_hwnd == IntPtr.Zero) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);

        var left = _anchor.X + _anchor.Width - width - 8;
        var top = _anchor.Y + _anchor.Height + 8;

        var virtualLeft = GetSystemMetrics(76);
        var virtualTop = GetSystemMetrics(77);
        var virtualRight = virtualLeft + GetSystemMetrics(78);
        var virtualBottom = virtualTop + GetSystemMetrics(79);

        if (top + height > virtualBottom - 4)
        {
            top = _anchor.Y - height - 8;
        }

        left = Math.Max(virtualLeft + 4, Math.Min(left, virtualRight - width - 4));
        top = Math.Max(virtualTop + 4, Math.Min(top, virtualBottom - height - 4));

        SetWindowPos(_hwnd, HwndTopmost, left, top, width, height, SwpNoActivate | SwpShowWindow);

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120));
        BeginAnimation(OpacityProperty, fadeIn);
    }

    private void FadeOut()
    {
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
