using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;

namespace LanFlow.Desktop.Presentation;

/// <summary>
/// 根据窗口所在屏幕（按窗口中心命中）返回该屏幕的工作区矩形，单位为 WPF 逻辑像素。
/// 在非交互/测试场景下可重写 <see cref="GetWorkArea"/> 以返回固定矩形。
/// </summary>
public class MonitorWorkAreaProvider
{
    public virtual Rect GetWorkArea(Window forWindow)
    {
        ArgumentNullException.ThrowIfNull(forWindow);

        var center = new System.Drawing.Point(
            (int)(forWindow.Left + forWindow.ActualWidth / 2),
            (int)(forWindow.Top + forWindow.ActualHeight / 2));
        var screen = Screen.FromPoint(center);
        var area = screen.WorkingArea;

        double scaleX = 1.0, scaleY = 1.0;
        if (PresentationSource.FromVisual(forWindow) is HwndSource hwnd && hwnd.RootVisual is Visual root)
        {
            var dpi = VisualTreeHelper.GetDpi(root);
            scaleX = dpi.PixelsPerInchX / 96.0;
            scaleY = dpi.PixelsPerInchY / 96.0;
        }

        return new Rect(area.Left / scaleX, area.Top / scaleY, area.Width / scaleX, area.Height / scaleY);
    }
}
