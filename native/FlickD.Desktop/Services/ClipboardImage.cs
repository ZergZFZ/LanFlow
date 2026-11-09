using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FlickD.Desktop.Services;

/// <summary>
/// 位图剪贴板写入。与 <see cref="ClipboardText"/> 同构：优先 Win32 CF_DIB 直写
/// （规避部分环境下 WPF/OLE 通道抛 CLIPBRD_E_CANT_OPEN），失败回退 OLE 并重试。
/// 输入须为冻结的 BitmapSource（调用方保证，截图合成产物均已 Freeze）。
/// </summary>
public static class ClipboardImage
{
    private const int CfDib = 8;
    private const uint GmemMoveable = 0x0002;
    // BITMAPINFOHEADER + 可选调色板位数的保守上限；DIB 头之后紧跟像素数据。
    private const int MaxInfoHeaderSize = 40 + 256 * 4;

    /// <summary>写入位图；返回是否成功。</summary>
    public static bool TrySet(BitmapSource image, int attempts = 4)
    {
        if (image is null || image.PixelWidth <= 0 || image.PixelHeight <= 0) return false;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (TrySetWin32(image)) return true;

            try
            {
                Clipboard.SetImage(image);
                return true;
            }
            catch
            {
                // OLE 也不可用，下一轮重试。
            }

            System.Threading.Thread.Sleep(45);
        }

        return false;
    }

    private static bool TrySetWin32(BitmapSource image)
    {
        byte[] dib;
        try
        {
            dib = ConvertToDib(image);
        }
        catch
        {
            return false;
        }

        // 像素较大的图（4K 截图 ≈ 33MB）GlobalAlloc 偶发失败，交由 OLE 回退。
        var hMem = GlobalAlloc(GmemMoveable, (uint)dib.Length);
        if (hMem == IntPtr.Zero) return false;

        var locked = false;
        var ownedByClipboard = false;
        try
        {
            var ptr = GlobalLock(hMem);
            if (ptr == IntPtr.Zero) return false;

            locked = true;
            Marshal.Copy(dib, 0, ptr, dib.Length);
            GlobalUnlock(hMem);
            locked = false;

            if (!OpenClipboard(IntPtr.Zero)) return false;
            try
            {
                if (!EmptyClipboard()) return false;
                if (SetClipboardData(CfDib, hMem) == IntPtr.Zero) return false;

                ownedByClipboard = true; // 所有权已转移给系统
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        finally
        {
            if (locked) GlobalUnlock(hMem);
            if (!ownedByClipboard) GlobalFree(hMem);
        }
    }

    /// <summary>
    /// BGRA32 像素 → CF_DIB（BITMAPINFOHEADER + 自底向上的 BGRX 像素，biCompression=BI_RGB）。
    /// WPF 的 Clipboard.SetImage 实际承载的也是 DIB；此处手工构造等价结构。
    /// </summary>
    private static byte[] ConvertToDib(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        // CF_DIB 要求像素行自底向上（负高度亦可表达自顶向下，但兼容性以正高度+翻转为佳）。
        var flipped = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var sourceRow = (height - 1 - y) * stride;
            Array.Copy(pixels, sourceRow, flipped, y * stride, stride);
        }

        // 32bpp BI_RGB 无调色板：头 40 字节 + 像素。
        var dib = new byte[40 + flipped.Length];
        BitConverter.GetBytes(40).CopyTo(dib, 0);                       // biSize
        BitConverter.GetBytes(width).CopyTo(dib, 4);                    // biWidth
        BitConverter.GetBytes(height).CopyTo(dib, 8);                   // biHeight（正=自底向上）
        BitConverter.GetBytes((short)1).CopyTo(dib, 12);                // biPlanes
        BitConverter.GetBytes((short)32).CopyTo(dib, 14);               // biBitCount
        BitConverter.GetBytes(0).CopyTo(dib, 16);                       // biCompression = BI_RGB
        BitConverter.GetBytes(flipped.Length).CopyTo(dib, 20);          // biSizeImage
        // 分辨率字段（16-24）与颜色字段（24-40）保持 0：BI_RGB 下可缺省。
        flipped.CopyTo(dib, 40);
        return dib;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(int format, IntPtr data);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, uint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);
}
