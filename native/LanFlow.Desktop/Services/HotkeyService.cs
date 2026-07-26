using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LanFlow.Desktop.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint VkSpace = 0x20;

    private HwndSource? _source;
    private Action? _onTriggered;

    public bool Register(Window window, Action onTriggered)
    {
        _source = PresentationSource.FromVisual(window) as HwndSource;
        if (_source is null)
        {
            return false;
        }

        _onTriggered = onTriggered;
        _source.AddHook(WindowProcedure);
        return RegisterHotKey(_source.Handle, HotkeyId, ModAlt, VkSpace);
    }

    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WindowProcedure);
        _source = null;
        _onTriggered = null;
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey || wParam.ToInt32() != HotkeyId)
        {
            return IntPtr.Zero;
        }

        _onTriggered?.Invoke();
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
