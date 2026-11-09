using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using FlickD.Desktop.Views;
using Xunit;

namespace FlickD.Desktop.Tests;

/// <summary>
/// 回归：Components.xaml 的隐式单行 TextBox 样式（Height=38）曾把取字结果窗的
/// 多行文本框压成 38px 单行条，文字被裁剪，弹窗显示为空。修复方式是显式
/// Style={x:Null} 走默认模板。本测试在自管 STA 线程上实例化窗口，断言结果框
/// 测量高度正常（多行）且未被隐式样式锁定。
/// </summary>
public sealed class OcrResultWindowTests
{
    [Fact]
    public void ResultBox_RendersMultiLine_HeightNotClampedByImplicitStyle()
    {
        RunOnStaThread(() =>
        {
            var window = new OcrResultWindow(string.Join("\n", Enumerable.Range(1, 19).Select(i => $"第{i}行 识别文本")));
            try
            {
                window.Show();

                var box = (TextBox)window.FindName("ResultBox")!;
                box.Measure(new Size(560, double.PositiveInfinity));

                // 19 行 14px 文本即使被包裹裁剪，期望高度也远超单行样式的 38px 锁定值。
                Assert.True(box.DesiredSize.Height > 100,
                    $"ResultBox 测量高度 {box.DesiredSize.Height:F1}px，疑似仍被隐式单行样式（Height=38）约束");

                // AcceptsReturn 是默认模板生效的多行前提。
                Assert.True(box.AcceptsReturn);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ResultBox_CaretAtStart_AfterShow()
    {
        RunOnStaThread(() =>
        {
            var window = new OcrResultWindow("第一行\n第二行");
            try
            {
                window.Show();
                // Show 同步完成布局，Loaded 已触发，光标应在开头（此前跳文末，长文本首屏为空）。
                Assert.Equal(0, ((TextBox)window.FindName("ResultBox")!).CaretIndex);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        Assert.True(thread.Join(0), "STA 测试线程 30 秒内未完成");
        if (failure is not null)
        {
            throw failure;
        }
    }
}
