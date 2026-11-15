using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FlickD.Desktop.Presentation;

// Windows 窗口合成材质：为主窗口启用 Acrylic 模糊（Win10 1709+ 的 SetWindowCompositionAttribute）。
// 远程桌面会话、省电模式或系统策略下可能失败，调用方须以返回值判断并降级为纯半透明。
public static class WindowCompositionHelper
{
    private const int AccentDisabled = 0;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int AccentFlagsDrawAllBorders = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    private enum WindowCompositionAttribute
    {
        WcaAccentPolicy = 19,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    // 启用 Acrylic 模糊。GradientColor 为 ABGR 打包的面板底色（alpha 决定模糊层浓度）。
    public static bool TryEnableAcrylic(Window window, Color baseColor, byte alpha)
    {
        ArgumentNullException.ThrowIfNull(window);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var accent = new AccentPolicy
        {
            AccentState = AccentEnableAcrylicBlurBehind,
            AccentFlags = AccentFlagsDrawAllBorders,
            GradientColor = (uint)((alpha << 24) | (baseColor.B << 16) | (baseColor.G << 8) | baseColor.R),
        };
        return SetAccent(hwnd, accent);
    }

    // 关闭合成材质，恢复默认（窗口表面由 WPF 自绘）。
    public static void Disable(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        SetAccent(hwnd, new AccentPolicy { AccentState = AccentDisabled });
    }

    private static bool SetAccent(IntPtr hwnd, AccentPolicy accent)
    {
        var accentPtr = IntPtr.Zero;
        try
        {
            accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf(accent));
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WcaAccentPolicy,
                Data = accentPtr,
                SizeOfData = Marshal.SizeOf(accent),
            };
            return SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (accentPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
    }
}
