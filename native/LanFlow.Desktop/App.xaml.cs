using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace LanFlow.Desktop;

public partial class App : Application
{
    private Forms.NotifyIcon? _trayIcon;

    internal bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 捕获 UI 线程未处理异常，避免右键菜单等渲染异常直接静默退出（闪退）。
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            MessageBox.Show(
                args.Exception.ToString(),
                "LanFlow 发生未处理异常",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        base.OnStartup(e);
        CreateTrayIcon();
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

        using var iconStream = typeof(App).Assembly.GetManifestResourceStream(
            "LanFlow.Desktop.Assets.LanFlow.ico")
            ?? throw new InvalidOperationException("未找到 LanFlow 系统托盘图标资源。");
        using var icon = new Icon(iconStream);

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "LanFlow",
            Icon = (Icon)icon.Clone(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
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
