using System;
using System.Windows;

namespace LanFlow.Desktop.Presentation;

/// <summary>
/// 计算并把设置窗口放置在主窗口右侧（贴近右上角、留边距），并夹取到所在屏幕工作区内。
/// </summary>
public sealed class SettingsWindowPlacement
{
    private readonly MonitorWorkAreaProvider _workAreaProvider;

    public SettingsWindowPlacement(MonitorWorkAreaProvider workAreaProvider)
    {
        _workAreaProvider = workAreaProvider ?? throw new ArgumentNullException(nameof(workAreaProvider));
    }

    public void Apply(Window settingsWindow, Window owner)
    {
        ArgumentNullException.ThrowIfNull(settingsWindow);
        ArgumentNullException.ThrowIfNull(owner);

        var workArea = _workAreaProvider.GetWorkArea(owner);
        settingsWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        var (x, y) = ComputeTopRight(workArea, owner, settingsWindow);
        settingsWindow.Left = x;
        settingsWindow.Top = y;
    }

    internal (double X, double Y) ComputeTopRight(Rect workArea, Window owner, Window settingsWindow)
    {
        double ownerWidth = owner.ActualWidth > 0 ? owner.ActualWidth : (double.IsNaN(owner.Width) ? 0 : owner.Width);
        double width = settingsWindow.ActualWidth > 0 ? settingsWindow.ActualWidth : (double.IsNaN(settingsWindow.Width) ? 0 : settingsWindow.Width);
        double height = settingsWindow.ActualHeight > 0 ? settingsWindow.ActualHeight : (double.IsNaN(settingsWindow.Height) ? 0 : settingsWindow.Height);

        return ComputeTopRight(workArea, owner.Left, ownerWidth, owner.Top, width, height);
    }

    public static (double X, double Y) ComputeTopRight(
        Rect workArea,
        double ownerLeft,
        double ownerWidth,
        double ownerTop,
        double settingsWidth,
        double settingsHeight)
    {
        const double margin = 16;
        double x = (ownerLeft + ownerWidth) - settingsWidth - margin;
        double y = ownerTop + margin;
        x = Math.Max(workArea.Left, Math.Min(x, workArea.Right - settingsWidth));
        y = Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - settingsHeight));
        return (x, y);
    }
}
