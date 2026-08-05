using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) => MainWindowInstance.Quit();

        var trayIcon = new TrayIcon
        {
            Icon = CreateTrayIcon(),
            ToolTipText = "LanFlow",
            Menu = new NativeMenu
            {
                showItem,
                new NativeMenuItemSeparator(),
                exitItem,
            },
        };
        trayIcon.Clicked += (_, _) => MainWindowInstance.ToggleVisibility();

        TrayIcon.GetIcons(this)?.Add(trayIcon);
    }

    private static unsafe WindowIcon CreateTrayIcon()
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
