namespace LanFlow.Desktop.Services;

/// <summary>
/// 全局热键注册失败后的重试策略。
/// 开机自启阶段组合键常被其他启动程序瞬时占用（错误码 1409），冲突方释放后即可注册成功，
/// 因此对 1409 采用无限退避重试；其他错误（配置、句柄等）重试无意义，应立即停止。
/// </summary>
public static class HotkeyRetryPolicy
{
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    // 退避序列（秒）：1s、2s、5s、15s、30s，之后封顶 60s 持续重试。
    private static readonly TimeSpan[] BackoffDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    ];

    /// <summary>
    /// 第 <paramref name="failedAttempts"/> 次失败之后，下一次重试应等待的时长。
    /// </summary>
    public static TimeSpan NextDelayAfterFailure(int failedAttempts)
    {
        if (failedAttempts < 0)
        {
            failedAttempts = 0;
        }

        var index = Math.Min(failedAttempts, BackoffDelays.Length - 1);
        return BackoffDelays[index];
    }

    /// <summary>
    /// 值得继续重试的失败：组合键被占用（1409，冲突方释放后可成功），
    /// 或窗口源尚未就绪（静默启动瞬间，句柄可用后可成功）。
    /// 其余失败（热键字符串非法、用户暂停等）是确定性问题，重试不会变好。
    /// </summary>
    public static bool IsRetryableFailure(HotkeyRegistrationFailure failureKind, int lastErrorCode)
        => failureKind == HotkeyRegistrationFailure.SourceNotReady
           || (failureKind == HotkeyRegistrationFailure.Win32 && lastErrorCode == ErrorHotkeyAlreadyRegistered);
}
