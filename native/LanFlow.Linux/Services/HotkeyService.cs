using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;

namespace LanFlow.Desktop.Services;

/// <summary>
/// Linux（X11）全局热键。
/// 单线程模型：所有 Xlib 调用（XOpenDisplay / XGrabKey / XNextEvent / XSync）都发生在
/// 专用循环线程上，UI 线程仅通过请求/应答（ManualResetEventSlim）与之通信。
/// Xlib 的 Display 不是线程安全的，绝不能两个线程并发操作同一个 Display
/// （round3.6 曾因 UI 线程与事件循环并发操作同一 Display，点「确定」重注册时死锁卡死）。
/// 被动抓取冲突以异步 BadAccess 协议错误上报，用 XSetErrorHandler+XSync 捕获判定。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int KeyPress = 2;
    private const uint AnyModifier = 1 << 7;
    private const uint ControlMask = 1 << 2;
    private const uint ShiftMask = 1 << 0;
    private const uint LockMask = 1 << 1;   // CapsLock
    private const uint Mod1Mask = 1 << 3;   // Alt
    private const uint Mod2Mask = 1 << 4;   // NumLock
    private const uint Mod4Mask = 1 << 6;   // Win / Super

    private Thread? _thread;
    private Action? _onTriggered;
    private volatile bool _running;

    // 仅循环线程使用的 Display
    private IntPtr _display;

    // UI 线程与循环线程之间的请求/应答
    private readonly object _gate = new();
    private uint _reqModifiers;
    private int _reqKeycode;
    private volatile bool _regrabPending;
    private bool _lastGrabOk;
    private int _lastGrabErrorCode;
    private readonly ManualResetEventSlim _regrabSignal = new(false);
    private readonly ManualResetEventSlim _grabDone = new(false);

    /// <summary>最近一次注册的结果说明（用于界面提示）。</summary>
    public string LastError { get; private set; } = string.Empty;

    // X11 错误回调：被动抓取冲突以异步 BadAccess 协议错误上报。委托存静态字段防 GC。
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

        return RequestGrab(modifiers, keycode);
    }

    public bool TryRegister(string hotkey)
    {
        if (!TryParse(hotkey, out var modifiers, out var keycode, out _))
        {
            LastError = "热键格式无效：" + hotkey;
            return false;
        }

        return RequestGrab(modifiers, keycode);
    }

    /// <summary>把抓取请求发给循环线程并等待结果（UI 线程不直接碰 Xlib）。</summary>
    private bool RequestGrab(uint modifiers, int keycode)
    {
        EnsureLoop();

        lock (_gate)
        {
            _reqModifiers = modifiers;
            _reqKeycode = keycode;
            _regrabPending = true;
        }

        _grabDone.Reset();
        _regrabSignal.Set();

        if (!_grabDone.Wait(3000))
        {
            LastError = "热键注册超时（X11 会话不可用？）";
            return false;
        }

        lock (_gate)
        {
            if (_lastGrabOk)
            {
                return true;
            }

            LastError = DescribeGrabFailure();
            return false;
        }
    }

    private void EnsureLoop()
    {
        if (_thread != null && _thread.IsAlive)
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

    /// <summary>循环线程：独占 Display，负责抓取与读取事件。</summary>
    private void Loop()
    {
        try
        {
            _display = XOpenDisplay(null);
        }
        catch
        {
            _display = IntPtr.Zero;
        }

        if (_display == IntPtr.Zero)
        {
            FailPending("X11 会话不可用，全局热键已停用");
            return;
        }

        var previous = XSetErrorHandler(_errorHandlerPtr);
        try
        {
            while (_running)
            {
                if (_regrabPending)
                {
                    uint mods;
                    int kc;
                    lock (_gate)
                    {
                        mods = _reqModifiers;
                        kc = _reqKeycode;
                        _regrabPending = false;
                    }

                    bool ok = DoGrab(mods, kc);
                    lock (_gate)
                    {
                        _lastGrabOk = ok;
                    }

                    _grabDone.Set();
                }

                // 轮询事件，避免 XNextEvent 阻塞导致无法响应重抓取请求
                if (XPending(_display) > 0)
                {
                    XEvent e;
                    XNextEvent(_display, out e);
                    if (e.type == KeyPress)
                    {
                        _onTriggered?.Invoke();
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }
        finally
        {
            XSetErrorHandler(previous);
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    private void FailPending(string message)
    {
        lock (_gate)
        {
            _lastGrabOk = false;
        }

        LastError = message;
        _grabDone.Set();
    }

    /// <summary>在循环线程上执行抓取：先清掉本客户端全部被动抓取，再逐变体抓取并以错误回路判定。</summary>
    private bool DoGrab(uint modifiers, int keycode)
    {
        var root = XDefaultRootWindow(_display);

        // 清空本客户端之前的所有被动抓取，避免换键残留
        XUngrabKey(_display, 0, AnyModifier, root);

        var variants = new[]
        {
            modifiers,
            modifiers | LockMask,
            modifiers | Mod2Mask,
            modifiers | LockMask | Mod2Mask,
        };

        var anySuccess = false;
        _lastGrabErrorCode = 0;
        foreach (var m in variants)
        {
            _xErrorCode = 0;
            XGrabKey(_display, keycode, m, root, false, 1, 1);
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

        return anySuccess;
    }

    private string DescribeGrabFailure()
        // BadAccess = 10：真被其它客户端占用；其它错误码多为映射/参数问题
        => _lastGrabErrorCode == 10
            ? "热键被占用，请更换为其他组合键"
            : $"热键注册失败（X11 错误码 {_lastGrabErrorCode}），请更换为其他组合键";

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

    public void Unregister()
    {
        _running = false;
        _regrabSignal.Set(); // 唤醒循环线程使其退出
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

        // keycode 查询用临时连接（仅 XStringToKeysym/XKeysymToKeycode，瞬间关闭，不与循环线程并发）。
        // 非 Linux 环境无 libX11 会抛 DllNotFoundException，须捕获并降级为 false。
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
        catch
        {
            return false;
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
    private static extern int XPending(IntPtr display);

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
