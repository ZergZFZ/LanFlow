using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;

namespace LanFlow.Desktop.Services;

/// <summary>
/// Linux（X11）全局热键：通过 libX11 的 XGrabKey 在根窗口上注册被动抓取，
/// 独立线程读取 XNextEvent 触发回调。Wayland 会话或缺少 X11 时静默降级（注册失败）。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int KeyPress = 2;
    private const uint ControlMask = 1 << 2;
    private const uint ShiftMask = 1 << 0;
    private const uint LockMask = 1 << 1;   // CapsLock
    private const uint Mod1Mask = 1 << 3;   // Alt
    private const uint Mod2Mask = 1 << 4;   // NumLock
    private const uint Mod4Mask = 1 << 6;   // Win / Super

    private IntPtr _display;
    private Thread? _thread;
    private Action? _onTriggered;
    private uint _modifiers;
    private int _keycode;
    private volatile bool _running;
    private int _lastGrabErrorCode;

    /// <summary>最近一次注册的结果说明（用于界面提示）。</summary>
    public string LastError { get; private set; } = string.Empty;

    // X11 错误回调：被动抓取冲突以异步 BadAccess 协议错误上报，必须用错误处理器捕获，
    // 不能依赖 XGrabKey 返回值（其成功返回 1）。委托存静态字段防 GC。
    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);
    private static readonly XErrorHandler _errorHandler = OnXError;
    private static readonly IntPtr _errorHandlerPtr = Marshal.GetFunctionPointerForDelegate(_errorHandler);
    private static volatile int _xErrorCode;

    private static int OnXError(IntPtr display, IntPtr errorEvent)
    {
        // XErrorEvent.error_code 在 64 位下偏移 24
        _xErrorCode = Marshal.ReadByte(errorEvent, 24);
        return 0;
    }

    public bool Register(Window window, Action onTriggered, string hotkey = "Ctrl+Alt+L")
    {
        _onTriggered = onTriggered;
        if (!TryParse(hotkey, out var modifiers, out var keycode, out _))
        {
            LastError = "热键格式无效：" + hotkey;
            return false;
        }

        if (_display == IntPtr.Zero)
        {
            try
            {
                _display = XOpenDisplay(null);
            }
            catch
            {
                _display = IntPtr.Zero;
            }
        }

        if (_display == IntPtr.Zero)
        {
            LastError = "X11 会话不可用，全局热键已停用";
            return false;
        }

        _modifiers = modifiers;
        _keycode = keycode;
        if (!Grab())
        {
            LastError = DescribeGrabFailure();
            return false;
        }

        StartLoop();
        return true;
    }

    public bool TryRegister(string hotkey)
    {
        if (_display == IntPtr.Zero)
        {
            LastError = "X11 会话不可用，全局热键已停用";
            return false;
        }

        if (!TryParse(hotkey, out var modifiers, out var keycode, out _))
        {
            LastError = "热键格式无效：" + hotkey;
            return false;
        }

        UngrabCurrent();

        _modifiers = modifiers;
        _keycode = keycode;
        if (!Grab())
        {
            LastError = DescribeGrabFailure();
            return false;
        }

        return true;
    }

    public static bool TryNormalize(string value, out string normalized)
    {
        normalized = string.Empty;
        if (!TryParse(value, out var modifiers, out _, out var token))
        {
            return false;
        }

        var tokens = new List<string>();
        if ((modifiers & ControlMask) != 0) tokens.Add("Ctrl");
        if ((modifiers & Mod1Mask) != 0) tokens.Add("Alt");
        if ((modifiers & ShiftMask) != 0) tokens.Add("Shift");
        if ((modifiers & Mod4Mask) != 0) tokens.Add("Win");
        tokens.Add(token);
        normalized = string.Join('+', tokens);
        return true;
    }

    // 返回 true 表示至少有一种修饰符变体抓取成功。
    // 判定依据是"XSync 后是否收到 X11 协议错误"，而非 XGrabKey 返回值。
    private bool Grab()
    {
        var root = XDefaultRootWindow(_display);
        var variants = new[]
        {
            _modifiers,
            _modifiers | LockMask,
            _modifiers | Mod2Mask,
            _modifiers | LockMask | Mod2Mask,
        };

        var previous = XSetErrorHandler(_errorHandlerPtr);
        var anySuccess = false;
        _lastGrabErrorCode = 0;
        try
        {
            foreach (var m in variants)
            {
                _xErrorCode = 0;
                XGrabKey(_display, _keycode, m, root, false, 1, 1);
                XSync(_display, false); // 强制投递异步错误（如 BadAccess）
                if (_xErrorCode == 0)
                {
                    anySuccess = true;
                }
                else
                {
                    _lastGrabErrorCode = _xErrorCode;
                    Console.WriteLine($"[LanFlow] XGrabKey mod=0x{m:X} 失败，X11 错误码={_xErrorCode}");
                }
            }
        }
        finally
        {
            XSetErrorHandler(previous);
        }

        return anySuccess;
    }

    private string DescribeGrabFailure()
        // BadAccess = 10：真被其它客户端占用；其它错误码多为映射/参数问题
        => _lastGrabErrorCode == 10
            ? "热键被占用，请更换为其他组合键"
            : $"热键注册失败（X11 错误码 {_lastGrabErrorCode}），请更换为其他组合键";

    private void UngrabCurrent()
    {
        if (_display == IntPtr.Zero || _keycode == 0)
        {
            return;
        }

        try
        {
            var root = XDefaultRootWindow(_display);
            foreach (var m in new[]
            {
                _modifiers,
                _modifiers | LockMask,
                _modifiers | Mod2Mask,
                _modifiers | LockMask | Mod2Mask,
            })
            {
                XUngrabKey(_display, _keycode, m, root);
            }
        }
        catch
        {
            // ignore
        }
    }

    private void StartLoop()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "LanFlowHotkey",
        };
        _thread.Start();
    }

    private void Loop()
    {
        while (_running && _display != IntPtr.Zero)
        {
            try
            {
                XEvent e;
                XNextEvent(_display, out e);
                if (e.type == KeyPress)
                {
                    _onTriggered?.Invoke();
                }
            }
            catch
            {
                break;
            }
        }
    }

    public void Unregister()
    {
        _running = false;
        if (_display == IntPtr.Zero)
        {
            return;
        }

        UngrabCurrent();

        try
        {
            XCloseDisplay(_display);
        }
        catch
        {
            // ignore
        }

        _display = IntPtr.Zero;
    }

    public void Dispose() => Unregister();

    private static bool TryParse(string value, out uint modifiers, out int keycode, out string token)
    {
        modifiers = 0;
        keycode = 0;
        token = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            return false;
        }

        foreach (var t in tokens[..^1])
        {
            if (t.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || t.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ControlMask;
            }
            else if (t.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= Mod1Mask;
            }
            else if (t.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ShiftMask;
            }
            else if (t.Equals("Win", StringComparison.OrdinalIgnoreCase) || t.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= Mod4Mask;
            }
            else
            {
                return false;
            }
        }

        if (modifiers == 0)
        {
            return false;
        }

        var keyToken = tokens[^1];
        var keysymName = ToKeysymName(keyToken);

        IntPtr display;
        try
        {
            display = XOpenDisplay(null);
        }
        catch
        {
            return false;
        }

        if (display == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var keysym = XStringToKeysym(keysymName);
            keycode = XKeysymToKeycode(display, keysym);
        }
        finally
        {
            XCloseDisplay(display);
        }

        if (keycode == 0)
        {
            return false;
        }

        token = keyToken;
        return true;
    }

    private static string ToKeysymName(string token)
    {
        return token.ToLowerInvariant() switch
        {
            "space" => "space",
            "enter" => "Return",
            "return" => "Return",
            "esc" => "Escape",
            "escape" => "Escape",
            "tab" => "Tab",
            "backspace" => "BackSpace",
            "delete" => "Delete",
            "up" => "Up",
            "down" => "Down",
            "left" => "Left",
            "right" => "Right",
            "insert" => "Insert",
            "home" => "Home",
            "end" => "End",
            "pageup" => "Prior",
            "pagedown" => "Next",
            _ => token,
        };
    }

    [StructLayout(LayoutKind.Sequential, Size = 192)]
    private struct XEvent
    {
        public int type;
    }

    [DllImport("libX11")]
    private static extern IntPtr XOpenDisplay(string? display);

    [DllImport("libX11")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11")]
    private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, bool ownerEvents, int pointerMode, int keyboardMode);

    [DllImport("libX11")]
    private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);

    [DllImport("libX11")]
    private static extern int XNextEvent(IntPtr display, out XEvent eventReturn);

    [DllImport("libX11")]
    private static extern IntPtr XStringToKeysym(string s);

    [DllImport("libX11")]
    private static extern int XKeysymToKeycode(IntPtr display, IntPtr keysym);

    [DllImport("libX11")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11")]
    private static extern int XSync(IntPtr display, bool discard);

    [DllImport("libX11")]
    private static extern IntPtr XSetErrorHandler(IntPtr handler);
}
