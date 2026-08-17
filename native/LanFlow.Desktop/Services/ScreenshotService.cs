using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace LanFlow.Desktop.Services;

/// <summary>一次截图的完整结果：整图 + 虚拟屏幕物理像素边界（Left/Top 可为负，对应多屏布局）。</summary>
public sealed record ScreenShotResult(BitmapSource Image, Int32Rect VirtualBounds);

/// <summary>
/// 截图服务：多屏拼接截图、选区裁剪、写入剪贴板。
/// 坐标系约定：全部使用「物理像素」——截图与选区均相对 VirtualBounds 的左上角。
/// </summary>
public static class ScreenshotService
{
    /// <summary>抓取所有屏幕并拼接为一张整图（物理像素）。失败返回 null。</summary>
    public static ScreenShotResult? CaptureAllScreens()
    {
        var bounds = Forms.Screen.AllScreens.Select(s => s.Bounds).ToArray();
        if (bounds.Length == 0) return null;

        var left = bounds.Min(b => b.Left);
        var top = bounds.Min(b => b.Top);
        var right = bounds.Max(b => b.Right);
        var bottom = bounds.Max(b => b.Bottom);
        var width = right - left;
        var height = bottom - top;

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            foreach (var b in bounds)
            {
                graphics.CopyFromScreen(b.Left, b.Top, b.Left - left, b.Top - top, b.Size);
            }
        }

        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return new ScreenShotResult(source, new Int32Rect(left, top, width, height));
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    /// <summary>将整图中相对虚拟屏幕的像素矩形裁剪并复制到剪贴板。</summary>
    public static bool CopySelection(ScreenShotResult shot, Int32Rect pixelRect)
    {
        var bounds = shot.VirtualBounds;
        // 裁剪坐标转为相对整图的像素偏移。
        var relative = new Int32Rect(
            pixelRect.X - bounds.X,
            pixelRect.Y - bounds.Y,
            pixelRect.Width,
            pixelRect.Height);
        if (relative.X < 0 || relative.Y < 0 ||
            relative.X + relative.Width > shot.Image.PixelWidth ||
            relative.Y + relative.Height > shot.Image.PixelHeight ||
            relative.Width <= 0 || relative.Height <= 0)
        {
            return false;
        }

        var cropped = new CroppedBitmap(shot.Image, relative);
        cropped.Freeze();
        Clipboard.SetImage(cropped);
        return true;
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
