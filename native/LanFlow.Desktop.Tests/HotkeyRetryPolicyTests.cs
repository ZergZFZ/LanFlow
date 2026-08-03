using System;
using LanFlow.Desktop.Services;
using Xunit;

namespace LanFlow.Desktop.Tests;

public class HotkeyRetryPolicyTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 30)]
    [InlineData(5, 60)]
    [InlineData(6, 60)]
    [InlineData(20, 60)]
    public void NextDelayAfterFailure_FollowsBackoffAndCapsAtOneMinute(int failedAttempts, int expectedSeconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            HotkeyRetryPolicy.NextDelayAfterFailure(failedAttempts));
    }

    [Theory]
    [InlineData(1409, true)]   // ERROR_HOTKEY_ALREADY_REGISTERED：冲突解除后即可成功，值得无限重试
    [InlineData(0, false)]     // 尚未失败 / 调用成功
    [InlineData(1408, false)]  // ERROR_INVALID_WINDOW_HANDLE：确定性错误
    [InlineData(87, false)]    // ERROR_INVALID_PARAMETER：确定性错误
    [InlineData(5, false)]     // ERROR_ACCESS_DENIED：确定性错误
    public void IsRetryableFailure_OnlyConflictIsRetryable(int errorCode, bool expected)
    {
        Assert.Equal(expected, HotkeyRetryPolicy.IsRetryableFailure(errorCode));
    }

    [Fact]
    public void ErrorConstants_MatchWin32ErrorHotkeyAlreadyRegistered()
    {
        Assert.Equal(1409, HotkeyRetryPolicy.ErrorHotkeyAlreadyRegistered);
        Assert.Equal(1409, HotkeyService.ErrorHotkeyAlreadyRegistered);
    }

    [Fact]
    public void NextDelayAfterFailure_NegativeAttempt_IsTreatedAsFirstFailure()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), HotkeyRetryPolicy.NextDelayAfterFailure(-1));
    }
}
