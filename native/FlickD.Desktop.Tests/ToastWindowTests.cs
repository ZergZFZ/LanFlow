using System;
using FlickD.Desktop.Views;
using Xunit;

namespace FlickD.Desktop.Tests;

public sealed class ToastWindowTests
{
    [Theory]
    [InlineData(1, 1400)]      // 短文案走基础时长
    [InlineData(12, 1400)]     // 阈值内不加时
    [InlineData(13, 1460)]     // 超阈值开始加时
    [InlineData(30, 2480)]     // 线性加时（1400 + 18×60）
    [InlineData(60, 3000)]     // 封顶 1400+1600
    [InlineData(120, 3000)]    // 超长封顶 3s
    public void ComputeHoldDuration_ScalesWithTextLength(int length, int expectedMs)
    {
        var duration = ToastWindow.ComputeHoldDuration(new string('字', length));
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), duration);
    }
}
