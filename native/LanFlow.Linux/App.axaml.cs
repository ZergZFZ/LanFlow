using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using LanFlow.Desktop;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop;

public class App : Application
{
    public MainWindow? MainWindowInstance { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 第三轮取证件（缺陷板 v2 §3.4）：UI 线程异常捕获。
        // 必须在 Avalonia 初始化完成后订阅（此处）——在 Program.Main 提前订阅会触发 D9。
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Console.WriteLine("[取证] UI线程未处理异常: " + e.Exception);
            e.Handled = true; // 记录后保活，避免静默崩溃丢失证据
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            MainWindowInstance = mainWindow;
            desktop.MainWindow = mainWindow;

            SetupTray();
            mainWindow.EnableHotkey();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyThemeColors(Settings settings)
    {
        if (Current is null)
        {
            return;
        }

        var colors = settings.ThemeColors;
        var resources = Current.Resources;
        // 浅色/深色切换必须同步 FluentTheme 变体，否则控件默认前景（Dark=白色系）
        // 在浅色面板上形成白底白字。
        Current.RequestedThemeVariant = settings.Theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        resources["PanelBrush"] = new SolidColorBrush(Color.Parse(colors.Panel));
        resources["PanelBorderBrush"] = new SolidColorBrush(Color.Parse(colors.PanelBorder));
        resources["SurfaceBrush"] = new SolidColorBrush(Color.Parse(colors.Surface));
        resources["SurfaceBorderBrush"] = new SolidColorBrush(Color.Parse(colors.SurfaceBorder));
        resources["FooterBrush"] = new SolidColorBrush(Color.Parse(colors.Footer));
        resources["TextPrimaryBrush"] = new SolidColorBrush(Color.Parse(colors.TextPrimary));
        resources["TextSecondaryBrush"] = new SolidColorBrush(Color.Parse(colors.TextSecondary));
        resources["AccentBrush"] = new SolidColorBrush(Color.Parse(colors.Accent));
        resources["HoverBrush"] = new SolidColorBrush(Color.Parse(colors.Hover));
        resources["IconSurfaceBrush"] = new SolidColorBrush(Color.Parse(colors.IconSurface));
        resources["BorderBrush"] = new SolidColorBrush(Color.Parse(colors.PanelBorder));
    }

    private void SetupTray()
    {
        if (MainWindowInstance is null)
        {
            return;
        }

        var showItem = new NativeMenuItem("显示 / 隐藏");
        showItem.Click += (_, _) => MainWindowInstance.ToggleVisibility();

        var restartItem = new NativeMenuItem("重启软件");
        restartItem.Click += (_, _) => MainWindowInstance.Restart();

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) => MainWindowInstance.Quit();

        var trayIcon = new TrayIcon
        {
            Icon = CreateTrayIcon(),
            ToolTipText = "LanFlow",
            Menu = new NativeMenu
            {
                showItem,
                restartItem,
                new NativeMenuItemSeparator(),
                exitItem,
            },
        };
        trayIcon.Clicked += (_, _) => MainWindowInstance.ToggleVisibility();

        TrayIcon.GetIcons(this)?.Add(trayIcon);
    }

    /// <summary>托盘图标：优先用随包发布的项目图标 lanflow.png（缩放到托盘尺寸），
    /// 避免早先纯色方块在托盘显示成「黑框」。</summary>
    private static WindowIcon CreateTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "lanflow.png");
            if (File.Exists(iconPath))
            {
                using var src = new Bitmap(iconPath);
                // scaled 交给 WindowIcon 持有，不能 using 释放，否则托盘图标拿到的位图已失效
                var scaled = src.CreateScaledBitmap(new PixelSize(32, 32), BitmapInterpolationMode.HighQuality);
                return new WindowIcon(scaled);
            }
        }
        catch
        {
            // 图标加载/缩放失败：回落纯色方块兜底
        }

        return CreateSolidIcon();
    }

    private static unsafe WindowIcon CreateSolidIcon()
    {
        const int size = 32;
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888);
        using (var framebuffer = bitmap.Lock())
        {
            var ptr = (byte*)framebuffer.Address.ToPointer();
            var length = size * size * 4;
            for (var i = 0; i < length; i += 4)
            {
                ptr[i] = 0x5E;
                ptr[i + 1] = 0x40;
                ptr[i + 2] = 0x35;
                ptr[i + 3] = 0xFF;
            }
        }

        return new WindowIcon(bitmap);
    }
}
