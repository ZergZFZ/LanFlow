using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlickD.Desktop.Models;
using FlickD.Desktop.Services;
using FlickD.Desktop.Views;
using Xunit;

namespace FlickD.Desktop.Tests;

/// <summary>
/// 回归：AnnotationVisualHost.Shapes 原为普通自动属性，setter 不触发重绘，
/// 撤销/重做等纯数据变化偶发不刷新（1.5.4 起撤销键"没作用"的根因）。
/// 现改为 AffectsRender 依赖属性。本测试在 STA 下渲染宿主元素为位图，
/// 断言撤销前后像素真实变化——验证的是"看得见的撤销"，不只是数据变化。
/// </summary>
public sealed class AnnotationVisualHostTests
{
    [Fact]
    public void Undo_MarksChange_IsVisibleInRenderedPixels()
    {
        RunOnStaThread(() =>
        {
            var host = new AnnotationVisualHost
            {
                Width = 200,
                Height = 100,
            };
            // 挂到可视树：RenderTargetBitmap 对不在 PresentationSource 里的元素渲染不出 OnRender 内容。
            var canvas = new Grid();
            canvas.Children.Add(host);
            var shell = new Window
            {
                Width = 240,
                Height = 140,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
                Content = canvas,
            };
            try
            {
                shell.Show();

                // 注入两个不透明矩形标注（列表实例保持同一份，模拟宿主真实用法）。
                var shapes = new System.Collections.Generic.List<AnnotationShape>
                {
                    NewRect(10, 10, 80, 80, Colors.Red),
                    NewRect(110, 10, 80, 80, Colors.Lime),
                };
                host.Shapes = shapes;

                // 基线：两个标注都在。
                var withTwo = RenderToPixels(host, 200, 100);

                // 撤销一个：从同一列表实例移除（属性值引用未变），显式请求重绘。
                shapes.RemoveAt(shapes.Count - 1);
                host.InvalidateVisual();
                var withOne = RenderToPixels(host, 200, 100);
                Assert.True(DiffRatio(withTwo, withOne) > 0.01,
                    "撤销一个标注后渲染未变化——撤销键失效的原始症状复现");

                // 再验证属性 setter 路径：换新列表恢复两个，渲染应回到两标注状态。
                var restored = new System.Collections.Generic.List<AnnotationShape>
                {
                    NewRect(10, 10, 80, 80, Colors.Red),
                    NewRect(110, 10, 80, 80, Colors.Lime),
                };
                host.Shapes = restored;
                var withTwoAgain = RenderToPixels(host, 200, 100);
                Assert.True(DiffRatio(withOne, withTwoAgain) > 0.01,
                    "重新赋值 Shapes 后渲染未变化——AffectsRender setter 路径失效");
            }
            finally
            {
                shell.Close();
            }
        });
    }

    [Fact]
    public void ShapesProperty_SetterChange_TriggersAffectsRender()
    {
        RunOnStaThread(() =>
        {
            var host = new AnnotationVisualHost { Width = 50, Height = 50 };
            var metadata = AnnotationVisualHost.ShapesProperty.GetMetadata(typeof(AnnotationVisualHost)) as FrameworkPropertyMetadata;
            Assert.NotNull(metadata);
            Assert.True(metadata!.AffectsRender, "ShapesProperty 必须声明 AffectsRender，否则 setter 变更不会触发重绘");
            var draftMetadata = AnnotationVisualHost.DraftProperty.GetMetadata(typeof(AnnotationVisualHost)) as FrameworkPropertyMetadata;
            Assert.NotNull(draftMetadata);
            Assert.True(draftMetadata!.AffectsRender, "DraftProperty 必须声明 AffectsRender");
        });
    }

    private static AnnotationShape NewRect(double x, double y, double w, double h, Color color) => new()
    {
        Tool = AnnotationTool.Rect,
        A = new Point(x, y),
        B = new Point(x + w, y + h),
        Color = color,
        StrokeWidth = 6,
    };

    private static byte[] RenderToPixels(UIElement element, int width, int height)
    {
        // 保证布局往返完成（Measure/Arrange + 强制 dispatcher 处理渲染队列），
        // 否则 RenderTargetBitmap 拿到的可能是空帧。
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(element);
        var pixels = new byte[width * height * 4];
        bmp.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static double DiffRatio(byte[] left, byte[] right)
    {
        var different = 0;
        var total = left.Length / 4;
        for (var i = 0; i < left.Length; i += 4)
        {
            if (Math.Abs(left[i] - right[i]) > 8 ||
                Math.Abs(left[i + 1] - right[i + 1]) > 8 ||
                Math.Abs(left[i + 2] - right[i + 2]) > 8)
            {
                different++;
            }
        }
        return (double)different / total;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA 测试线程 30 秒内未完成");
        if (failure is not null) throw failure;
    }
}
