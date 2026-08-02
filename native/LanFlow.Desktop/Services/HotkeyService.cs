using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace LanFlow.Desktop.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private HwndSource? _source;
    private Action? _onTriggered;
    private uint _modifiers;
    private uint _virtualKey;
    private bool _isRegistered;
    private int _lastErrorCode;

    // 最近一次 RegisterHotKey/UnregisterHotKey 的 Win32 错误码（GetLastError）。
    // 0 表示最近一次调用成功或尚未调用；供诊断“开机自启注册失败”使用。
    public int LastErrorCode => _lastErrorCode;

    public bool Register(Window window, Action onTriggered, string hotkey = "Alt+Space")
    {
        _source = PresentationSource.FromVisual(window) as HwndSource;
        if (_source is null || !TryParse(hotkey, out var modifiers, out var virtualKey)) return false;
        _onTriggered = onTriggered;
        _source.AddHook(WindowProcedure);
        _modifiers = modifiers;
        _virtualKey = virtualKey;
        _isRegistered = TryRegisterHotKey(_source.Handle, HotkeyId, modifiers, virtualKey);
        return _isRegistered;
    }

    public bool TryRegister(string hotkey)
    {
        if (_source is null || !TryParse(hotkey, out var modifiers, out var virtualKey)) return false;
        if (_isRegistered && modifiers == _modifiers && virtualKey == _virtualKey) return true;

        var oldModifiers = _modifiers;
        var oldVirtualKey = _virtualKey;
        var hadOldRegistration = _isRegistered;
        if (hadOldRegistration) UnregisterHotKey(_source.Handle, HotkeyId);

        if (TryRegisterHotKey(_source.Handle, HotkeyId, modifiers, virtualKey))
        {
            _modifiers = modifiers;
            _virtualKey = virtualKey;
            _isRegistered = true;
            return true;
        }

        _isRegistered = hadOldRegistration && TryRegisterHotKey(_source.Handle, HotkeyId, oldModifiers, oldVirtualKey);
        _modifiers = oldModifiers;
        _virtualKey = oldVirtualKey;
        return false;
    }

    public bool IsEnabled => _isRegistered;

    // 暂停/恢复全局快捷键：暂停时不影响窗口与托盘其他功能。
    public bool SetEnabled(bool enabled)
    {
        if (_source is null)
        {
            return false;
        }

        if (enabled == _isRegistered)
        {
            return true;
        }

        if (enabled)
        {
            _isRegistered = TryRegisterHotKey(_source.Handle, HotkeyId, _modifiers, _virtualKey);
            return _isRegistered;
        }

        if (_isRegistered)
        {
            TryUnregisterHotKey(_source.Handle, HotkeyId);
        }

        _isRegistered = false;
        return true;
    }

    public static bool TryNormalize(string value, out string normalized)
    {
        normalized = string.Empty;
        if (!TryParse(value, out var modifiers, out var virtualKey)) return false;
        var tokens = new List<string>();
        if ((modifiers & ModControl) != 0) tokens.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) tokens.Add("Alt");
        if ((modifiers & ModShift) != 0) tokens.Add("Shift");
        if ((modifiers & ModWin) != 0) tokens.Add("Win");
        tokens.Add(KeyInterop.KeyFromVirtualKey((int)virtualKey).ToString().Replace("Oem", string.Empty));
        normalized = string.Join('+', tokens);
        return true;
    }

    private static bool TryParse(string value, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var tokens = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return false;

        foreach (var token in tokens[..^1])
        {
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || token.Equals("Control", StringComparison.OrdinalIgnoreCase)) modifiers |= ModControl;
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= ModAlt;
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= ModShift;
            else if (token.Equals("Win", StringComparison.OrdinalIgnoreCase) || token.Equals("Windows", StringComparison.OrdinalIgnoreCase)) modifiers |= ModWin;
            else return false;
        }

        if (modifiers == 0 || !Enum.TryParse<Key>(tokens[^1], ignoreCase: true, out var key)) return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }

    public void Dispose()
    {
        if (_source is null) return;
        if (_isRegistered) TryUnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WindowProcedure);
        _source = null;
        _onTriggered = null;
        _isRegistered = false;
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey || wParam.ToInt32() != HotkeyId) return IntPtr.Zero;
        _onTriggered?.Invoke();
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private bool TryRegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey)
    {
        _lastErrorCode = 0;
        var result = RegisterHotKey(hwnd, id, modifiers, virtualKey);
        if (!result)
        {
            _lastErrorCode = Marshal.GetLastWin32Error();
        }

        return result;
    }

    private void TryUnregisterHotKey(IntPtr hwnd, int id)
    {
        _lastErrorCode = 0;
        if (!UnregisterHotKey(hwnd, id))
        {
            _lastErrorCode = Marshal.GetLastWin32Error();
        }
    }
}
