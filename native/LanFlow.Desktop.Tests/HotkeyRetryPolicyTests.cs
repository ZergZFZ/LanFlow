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
    [InlineData(HotkeyRegistrationFailure.Win32, 1409, true)]     // 被占用：冲突解除后即可成功
    [InlineData(HotkeyRegistrationFailure.SourceNotReady, 0, true)] // 静默启动窗口源未就绪：可稍后重试
    [InlineData(HotkeyRegistrationFailure.Win32, 0, false)]        // Win32 失败但无错误码：非占用
    [InlineData(HotkeyRegistrationFailure.Win32, 1408, false)]     // ERROR_INVALID_WINDOW_HANDLE
    [InlineData(HotkeyRegistrationFailure.Win32, 87, false)]       // ERROR_INVALID_PARAMETER
    [InlineData(HotkeyRegistrationFailure.InvalidHotkey, 0, false)]
    [InlineData(HotkeyRegistrationFailure.Paused, 0, false)]
    [InlineData(HotkeyRegistrationFailure.None, 0, false)]
    public void IsRetryableFailure_OnlyTransientCausesAreRetryable(
        HotkeyRegistrationFailure failureKind,
        int errorCode,
        bool expected)
    {
        Assert.Equal(expected, HotkeyRetryPolicy.IsRetryableFailure(failureKind, errorCode));
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
