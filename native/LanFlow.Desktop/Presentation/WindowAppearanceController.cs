using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Presentation;

public sealed class WindowAppearanceController
{
    private const int GwlStyle = -16;
    private const int WsThickFrame = 0x00040000;
    private const int SwpFrameChanged = 0x0020;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoZOrder = 0x0004;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }
    private static readonly string[] LayeredSurfaceBrushKeys =
    [
        "WindowBackgroundBrush",
        "SurfaceBrush",
        "MutedSurfaceBrush",
    ];

    public void EnableNativeShadow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        int style = GetWindowLong(hwnd, GwlStyle);
        SetWindowLong(hwnd, GwlStyle, style | WsThickFrame);

        var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder);
    }

    public static WindowAppearanceState Calculate(
        string? mode,
        double layeredOpacity,
        double wholeWindowOpacity)
    {
        layeredOpacity = Math.Clamp(layeredOpacity, 0.40, 1.00);
        wholeWindowOpacity = Math.Clamp(wholeWindowOpacity, 0.40, 1.00);

        return string.Equals(
            mode,
            SettingsOptionValues.TransparencyWholeWindow,
            StringComparison.Ordinal)
            ? new WindowAppearanceState(wholeWindowOpacity, byte.MaxValue, 1.0)
            : new WindowAppearanceState(
                1.0,
                (byte)Math.Round(layeredOpacity * byte.MaxValue, MidpointRounding.AwayFromZero),
                1.0);
    }

    public void Apply(
        Window window,
        FrameworkElement surfaceRoot,
        FrameworkElement contentRoot,
        Settings settings)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(surfaceRoot);
        ArgumentNullException.ThrowIfNull(contentRoot);
        ArgumentNullException.ThrowIfNull(settings);

        var state = Calculate(
            settings.TransparencyMode,
            settings.LayeredOpacity,
            settings.WholeWindowOpacity);

        window.Opacity = state.WindowOpacity;
        surfaceRoot.Opacity = 1.0;
        contentRoot.Opacity = state.ContentOpacity;

        foreach (var key in LayeredSurfaceBrushKeys)
        {
            if (window.TryFindResource(key) is not SolidColorBrush source)
            {
                continue;
            }

            surfaceRoot.Resources[key] = CreateBrush(source.Color, state.SurfaceAlpha);
            contentRoot.Resources[key] = CreateBrush(source.Color, state.SurfaceAlpha);
        }
    }

    private static SolidColorBrush CreateBrush(Color source, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, source.R, source.G, source.B));
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }
}
