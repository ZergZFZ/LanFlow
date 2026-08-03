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

    // RegisterHotKey 失败错误码：组合键已被当前进程的其他线程或其他进程占用。
    // 这是唯一值得无限重试的失败原因——冲突方释放后即可注册成功。
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    private HwndSource? _source;
    private HwndSource? _registeredSource;
    private Action? _onTriggered;
    private uint _modifiers;
    private uint _virtualKey;
    private bool _isRegistered;
    private bool _hookAdded;
    private bool _isPaused;
    private int _lastErrorCode;

    // 最近一次 RegisterHotKey/UnregisterHotKey 的 Win32 错误码（GetLastError）。
    // 0 表示最近一次调用成功或尚未调用；供诊断“开机自启注册失败”使用。
    public int LastErrorCode => _lastErrorCode;

    /// <summary>
    /// 用户通过托盘“暂停全局快捷键”后为 true：重试循环必须尊重暂停状态，不能自动恢复。
    /// </summary>
    public bool IsPaused => _isPaused;

    public bool Register(Window window, Action onTriggered, string hotkey = "Alt+Space")
    {
        var source = PresentationSource.FromVisual(window) as HwndSource;
        if (source is null || !TryParse(hotkey, out var modifiers, out var virtualKey))
        {
            return false;
        }

        _onTriggered = onTriggered;

        // 窗口句柄被重建（例如关闭后重新显示）时，旧注册随旧句柄一并销毁，必须重新注册。
        if (!ReferenceEquals(source, _registeredSource))
        {
            _isRegistered = false;
            _registeredSource = source;
            _hookAdded = false;
        }

        // 钩子只挂一次：反复 Register（重试、窗口重建）不能叠加多个 WindowProcedure。
        if (!_hookAdded)
        {
            source.AddHook(WindowProcedure);
            _hookAdded = true;
        }

        _source = source;
        _modifiers = modifiers;
        _virtualKey = virtualKey;

        if (_isPaused)
        {
            return false;
        }

        // 同一句柄、同一组合键已注册成功时幂等返回，避免重复 RegisterHotKey 触发 1409。
        if (_isRegistered)
        {
            return true;
        }

        _isRegistered = TryRegisterHotKey(source.Handle, HotkeyId, modifiers, virtualKey);
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
            // 设置里显式更换快捷键视为“启用”动作，清除托盘暂停状态。
            _isPaused = false;
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

        if (enabled)
        {
            _isPaused = false;
            if (_isRegistered)
            {
                return true;
            }

            _isRegistered = TryRegisterHotKey(_source.Handle, HotkeyId, _modifiers, _virtualKey);
            return _isRegistered;
        }

        _isPaused = true;
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
        _registeredSource = null;
        _onTriggered = null;
        _isRegistered = false;
        _isPaused = false;
        _hookAdded = false;
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
