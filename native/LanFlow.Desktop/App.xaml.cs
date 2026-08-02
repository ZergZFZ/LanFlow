using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop;

public partial class App : Application
{
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _toggleHotkeyMenuItem;

    internal bool IsExiting { get; private set; }

    // 被系统开机拉起（带 --silent 参数）时为 true：仅驻留托盘，不显示主窗口。
    public static bool IsSilentStart { get; private set; }

    public App()
    {
        // 托盘类应用：仅在显式退出时关闭进程，避免静默启动时因无可见窗口而直接退出。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 兜底：任何线程上的未处理异常都先写日志再提示，避免“快速异常检测失败 / 进程立即终止”的静默崩溃。
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            WriteCrashLog("AppDomain.UnhandledException", ex);
            SafeShowCrash(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 捕获 UI 线程未处理异常，避免右键菜单等渲染异常直接静默退出（闪退）。
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            WriteCrashLog("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(
                args.Exception.ToString(),
                "LanFlow 发生未处理异常",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        try
        {
            base.OnStartup(e);
            ResolveConfigLocationAtStartup();
            CreateTrayIcon();

            IsSilentStart = e.Args.Any(a =>
                string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase));

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            if (IsSilentStart)
            {
                // 静默启动：仅创建窗口句柄（用于注册全局热键、DWM 阴影挂钩与托盘交互），
                // 不调用 Show()，避免开机后主窗口弹出占据屏幕。
                new WindowInteropHelper(mainWindow).EnsureHandle();
            }
            else
            {
                // 延迟到 Dispatcher 消息循环启动后再 Show。
                // 规避极少数情况下（尤其由更新脚本 start 拉起时）在 OnStartup 内直接 Show()
                // 触发的 “VisualTarget 的根 Visual 不能具有父级” 异常（WPF 视觉树时序竞争）。
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() => ShowMainWindowSafe(mainWindow)));
            }
        }
        catch (Exception ex)
        {
            // 启动期异常（Dispatcher 循环启动前抛出）不会被 DispatcherUnhandledException 捕获，
            // 这里兜底，避免未处理异常触发 FailFast。
            WriteCrashLog("OnStartup", ex);
            MessageBox.Show(
                "LanFlow 启动失败：" + ex.Message + "\n\n详细信息已写入：" + CrashLogPath,
                "LanFlow 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            // 启动已失败，主动退出，避免残留为无主窗口的托盘僵尸进程。
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示启动器", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _toggleHotkeyMenuItem = new Forms.ToolStripMenuItem("暂停全局快捷键", null, (_, _) => ToggleHotkeyFromTray());
        menu.Items.Add(_toggleHotkeyMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 LanFlow", null, (_, _) => RequestShutdown());

        Icon icon;
        using (var iconStream = typeof(App).Assembly.GetManifestResourceStream(
                   "LanFlow.Desktop.Assets.LanFlow.ico"))
        {
            if (iconStream is null)
            {
                // 资源缺失不应导致启动失败：回退到系统图标。
                icon = (Icon)SystemIcons.Application.Clone();
            }
            else
            {
                icon = new Icon(iconStream);
            }
        }

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "LanFlow",
            Icon = icon,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ToggleHotkeyFromTray()
    {
        if (MainWindow is MainWindow mw)
        {
            var message = mw.ToggleHotkeyEnabled();
            _toggleHotkeyMenuItem!.Text = mw.IsHotkeyEnabled ? "暂停全局快捷键" : "恢复全局快捷键";
            mw.SetStatusText(message);
        }
    }

    // 热键多次注册失败后的托盘提示：静默自启时用户看不到主窗口状态栏。
    public void NotifyHotkeyRegistrationFailed()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.ShowBalloonTip(
            4000,
            "LanFlow",
            "全局快捷键注册失败，请在设置中更换组合键。",
            Forms.ToolTipIcon.Warning);
    }

    private static string CrashLogPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanFlow");
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "crash.log");
        }
    }

    private static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}";
            File.AppendAllText(CrashLogPath, line + Environment.NewLine + Environment.NewLine);
        }
        catch { }
    }

    private static string DiagnosticLogPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanFlow");
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "config-diagnostics.log");
        }
    }

    private static void WriteDiagnosticLog(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            File.AppendAllText(DiagnosticLogPath, line + Environment.NewLine);
        }
        catch { }
    }

    /// <summary>
    /// 启动时解析配置目录（默认或 locator 指定），确保目录存在并写入诊断日志。
    /// </summary>
    private static void ResolveConfigLocationAtStartup()
    {
        try
        {
            var location = new ConfigLocationService();
            var resolution = location.Resolve();
            Directory.CreateDirectory(resolution.DirectoryPath);
            WriteDiagnosticLog(
                "Config directory=" + resolution.DirectoryPath +
                "; isDefault=" + resolution.IsDefault +
                "; exists=" + File.Exists(resolution.ConfigPath) +
                "; warning=" + (resolution.Warning ?? "none"));
        }
        catch (Exception ex)
        {
            WriteDiagnosticLog("ResolveConfigLocationAtStartup failed: " + ex.Message);
        }
    }

    private static void SafeShowCrash(Exception? ex)
    {
        try
        {
            MessageBox.Show(
                "LanFlow 发生了严重错误，进程即将退出：\n\n" + (ex?.ToString() ?? "未知错误") +
                "\n\n日志已写入：" + CrashLogPath,
                "LanFlow 严重错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { }
    }

    private void RequestShutdown()
    {
        IsExiting = true;
        Shutdown();
    }

    private void ShowMainWindow()
    {
        if (MainWindow is null)
        {
            return;
        }

        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
        MainWindow.Focus();

        if (MainWindow is MainWindow mw)
        {
            mw.FocusSearch();
        }
    }

    // 首次显示主窗口：规避 “根 Visual 不能具有父级” 的偶发时序竞争。
    // 若 Show 失败，重建窗口重试一次；仍失败则记录日志并退出，避免僵尸进程。
    private void ShowMainWindowSafe(MainWindow mainWindow)
    {
        try
        {
            mainWindow.Show();
            return;
        }
        catch (Exception ex) when (IsRootVisualParentError(ex))
        {
            WriteCrashLog("OnStartup.Show", ex);
        }
        catch (Exception ex)
        {
            WriteCrashLog("OnStartup.Show", ex);
            MessageBox.Show(
                "LanFlow 启动失败：" + ex.Message + "\n\n详细信息已写入：" + CrashLogPath,
                "LanFlow 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // 罕见：根 Visual 已被误挂载，重建窗口重试一次。
        try
        {
            var fresh = new MainWindow();
            MainWindow = fresh;
            fresh.Show();
        }
        catch (Exception ex)
        {
            WriteCrashLog("OnStartup.ShowRetry", ex);
            MessageBox.Show(
                "LanFlow 启动失败：" + ex.Message + "\n\n详细信息已写入：" + CrashLogPath,
                "LanFlow 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static bool IsRootVisualParentError(Exception ex) =>
        ex is ArgumentException &&
        (ex.Message.Contains("父级") || ex.Message.Contains("parent", StringComparison.OrdinalIgnoreCase));
}
