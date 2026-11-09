using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FlickD.Desktop.Services;

/// <summary>
/// 纯文本剪贴板写入。
/// 部分环境（剪贴板被安全软件/输入法/远程控制占用）下 WPF 的 OLE 剪贴板会抛
/// CLIPBRD_E_CANT_OPEN（0x800401D0），此时退回 Win32 CF_UNICODETEXT 通道。
/// </summary>
public static class ClipboardText
{
    private const int CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    /// <summary>写入纯文本；返回是否成功。</summary>
    public static bool TrySet(string text, int attempts = 4)
    {
        if (string.IsNullOrEmpty(text)) return false;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            // 优先 Win32：本机 WPF 的 OLE 通道会把剪贴板留成 OLE 私有格式（无 CF_UNICODETEXT）。
            if (TrySetWin32(text)) return true;

            try
            {
                System.Windows.Clipboard.SetText(text);
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

    private static bool TrySetWin32(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var hMem = GlobalAlloc(GmemMoveable, (uint)bytes.Length);
        if (hMem == IntPtr.Zero) return false;

        var locked = false;
        var ownedByClipboard = false;
        try
        {
            var ptr = GlobalLock(hMem);
            if (ptr == IntPtr.Zero) return false;

            locked = true;
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            GlobalUnlock(hMem);
            locked = false;

            if (!OpenClipboard(IntPtr.Zero)) return false;
            try
            {
                if (!EmptyClipboard()) return false;
                if (SetClipboardData(CfUnicodeText, hMem) == IntPtr.Zero) return false;

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
