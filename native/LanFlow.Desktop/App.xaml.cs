using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace LanFlow.Desktop;

public partial class App : Application
{
    private Forms.NotifyIcon? _trayIcon;

    internal bool IsExiting { get; private set; }

    public App()
    {
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
            CreateTrayIcon();
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
    }
}
