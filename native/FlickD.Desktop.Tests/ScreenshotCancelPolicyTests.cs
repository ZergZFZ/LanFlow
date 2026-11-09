using FlickD.Desktop.Views;
using Xunit;

namespace FlickD.Desktop.Tests;

/// <summary>
/// Esc 与右键共用同一取消语义。此测试锁定决策规则，防止两条输入路径行为漂移
/// （例如某天只改 Esc 分支而忘了右键）。
/// </summary>
public sealed class ScreenshotCancelPolicyTests
{
    [Theory]
    [InlineData(true, false, CancelAction.CancelText)]   // 文字输入中
    [InlineData(true, true, CancelAction.CancelText)]    // 文字输入中（标注态优先归文字）
    [InlineData(false, true, CancelAction.ExitAnnotate)] // 标注态 → 返回框选
    [InlineData(false, false, CancelAction.CloseWindow)] // 选择态 → 关闭截图
    public void Decide_MatchesEscSemanticsAtEveryPhase(bool hasTextEditor, bool isAnnotating, CancelAction expected)
    {
        Assert.Equal(expected, ScreenshotCancelPolicy.Decide(hasTextEditor, isAnnotating));
    }
}
