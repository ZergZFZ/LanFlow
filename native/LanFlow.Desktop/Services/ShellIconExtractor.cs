using System;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LanFlow.Desktop.Services;

public sealed class ShellIconExtractor : IIconExtractor
{
    public async ValueTask<ImageSource?> ExtractAsync(
        string path,
        int pixelSize,
        CancellationToken cancellationToken)
    {
        // 开机早期 Windows Shell 图标缓存可能尚未就绪，SHGetFileInfo 偶发失败。
        // 失败后做少量短延迟重试，尽量在 Shell 就绪后拿到图标，避免失败结果长期停留在界面上。
        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var image = await Task.Run(
                () => Extract(path, Math.Max(1, pixelSize), cancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (image is not null)
            {
                if (image.CanFreeze && !image.IsFrozen) image.Freeze();
                return image;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (attempt < maxAttempts - 1)
            {
                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private static ImageSource? Extract(string path, int pixelSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var exists = File.Exists(path) || Directory.Exists(path);
            var flags = ShellFileInfoFlags.Icon | ShellFileInfoFlags.LargeIcon;
            var attributes = exists ? 0u : FileAttributeNormal;
            var iconPath = exists ? path : Path.GetExtension(path);
            if (!exists) flags |= ShellFileInfoFlags.UseFileAttributes;

            var result = SHGetFileInfo(
                iconPath,
                attributes,
                out var info,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                flags);
            if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero) return null;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var icon = Icon.FromHandle(info.IconHandle);
                return Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(pixelSize, pixelSize));
            }
            finally
            {
                DestroyIcon(info.IconHandle);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private const uint FileAttributeNormal = 0x80;

    [Flags]
    private enum ShellFileInfoFlags : uint
    {
        Icon = 0x000000100,
        LargeIcon = 0x000000000,
        UseFileAttributes = 0x000000010,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ShellFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        out ShellFileInfo fileInfo,
        uint cbFileInfo,
        ShellFileInfoFlags flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
