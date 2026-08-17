using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace LanFlow.Desktop.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private readonly int _hotkeyId;
    private HwndSource? _source;
    private Action? _onTriggered;
    private uint _modifiers;
    private uint _virtualKey;
    private bool _isRegistered;
    private bool _hookAdded;

    /// <summary>创建一个全局热键注册器。同一窗口可注册多个实例，每个实例需使用不同的 hotkeyId（如主窗=1，截图=2）。</summary>
    public HotkeyService(int hotkeyId = 1)
    {
        _hotkeyId = hotkeyId;
    }

    public bool IsRegistered => _isRegistered;

    public bool Register(Window window, Action onTriggered, string hotkey = "Alt+Space")
    {
        _source = PresentationSource.FromVisual(window) as HwndSource;
        if (_source is null || !TryParse(hotkey, out var modifiers, out var virtualKey)) return false;
        _onTriggered = onTriggered;
        _source.AddHook(WindowProcedure);
        _hookAdded = true;
        _modifiers = modifiers;
        _virtualKey = virtualKey;
        _isRegistered = RegisterHotKey(_source.Handle, _hotkeyId, modifiers, virtualKey);
        return _isRegistered;
    }

    public bool TryRegister(string hotkey)
    {
        if (_source is null || !TryParse(hotkey, out var modifiers, out var virtualKey)) return false;
        if (_isRegistered && modifiers == _modifiers && virtualKey == _virtualKey) return true;

        var oldModifiers = _modifiers;
        var oldVirtualKey = _virtualKey;
        var hadOldRegistration = _isRegistered;
        if (hadOldRegistration) UnregisterHotKey(_source.Handle, _hotkeyId);

        if (RegisterHotKey(_source.Handle, _hotkeyId, modifiers, virtualKey))
        {
            _modifiers = modifiers;
            _virtualKey = virtualKey;
            _isRegistered = true;
            return true;
        }

        _isRegistered = hadOldRegistration && RegisterHotKey(_source.Handle, _hotkeyId, oldModifiers, oldVirtualKey);
        _modifiers = oldModifiers;
        _virtualKey = oldVirtualKey;
        return false;
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
        if (_isRegistered) UnregisterHotKey(_source.Handle, _hotkeyId);
        if (_hookAdded) _source.RemoveHook(WindowProcedure);
        _source = null;
        _onTriggered = null;
        _isRegistered = false;
        _hookAdded = false;
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey || wParam.ToInt32() != _hotkeyId) return IntPtr.Zero;
        _onTriggered?.Invoke();
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
