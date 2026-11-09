using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using FlickD.Desktop.Services;
using Xunit;

namespace FlickD.Desktop.Tests;

// 管道协议端到端测试：同一进程内真实命名管道，不依赖 WPF Application 与真实互斥量名。
public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void IsShowCommand_MatchesOnlyShow()
    {
        Assert.True(SingleInstanceGuard.IsShowCommand("show"));
        Assert.True(SingleInstanceGuard.IsShowCommand(" SHOW \r\n"));
        Assert.False(SingleInstanceGuard.IsShowCommand("hide"));
        Assert.False(SingleInstanceGuard.IsShowCommand(null));
    }

    [Fact]
    public async Task Listener_RespondsOk_AndRunsShowAction_ForShowCommand()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var shown = 0;
        using var guard = CreateGuard(suffix);
        guard.TryAcquire(() => shown++);

        var response = await SendAndReadAsync(SingleInstanceGuard.PipeNameFor(suffix), "show");

        Assert.Equal("ok", response);
        Assert.Equal(1, shown);
    }

    [Fact]
    public async Task Listener_IgnoresUnknownCommand_AndClosesWithoutResponse()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var shown = 0;
        using var guard = CreateGuard(suffix);
        guard.TryAcquire(() => shown++);

        var response = await SendAndReadAsync(SingleInstanceGuard.PipeNameFor(suffix), "bogus");

        Assert.Null(response);
        Assert.Equal(0, shown);
    }

    [Fact]
    public void NotifyExistingShow_WakesExistingInstance_AndReceivesOk()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var shown = 0;
        using var server = CreateGuard(suffix);
        server.TryAcquire(() => shown++);

        var logs = new List<string>();
        using var client = CreateGuard(suffix, logs);
        client.NotifyExistingShow();

        Assert.Equal(1, shown);
        Assert.Contains(logs, line => line.Contains("show-response=ok"));
    }

    [Fact]
    public void NotifyExistingShow_WithoutServer_FailsFast_AndLogsFailure()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var logs = new List<string>();
        using var client = CreateGuard(suffix, logs, connectTimeout: TimeSpan.FromMilliseconds(150));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        client.NotifyExistingShow();
        stopwatch.Stop();

        Assert.Contains(logs, line => line.Contains("show-failed"));
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"应快速失败而非长时间阻塞，实际 {stopwatch.ElapsedMilliseconds}ms");
    }

    private static SingleInstanceGuard CreateGuard(
        string suffix,
        List<string>? logs = null,
        TimeSpan? connectTimeout = null) =>
        new(
            uiExecutor: action => action(),
            log: logs is null ? null : logs.Add,
            connectTimeout: connectTimeout ?? TimeSpan.FromSeconds(3),
            responseTimeout: TimeSpan.FromSeconds(3),
            nameSuffix: suffix);

    private static async Task<string?> SendAndReadAsync(string pipeName, string command)
    {
        using var client = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);

        // 不使用 using：服务端可能在客户端读取完成前就关闭连接，
        // 此时 writer.Dispose 的最后一次 flush 会抛 "Pipe is broken"，随流关闭即可。
        var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
        };
        await writer.WriteLineAsync(command);

        var readTask = Task.Run(() =>
            new StreamReader(client, new UTF8Encoding(false), leaveOpen: true).ReadLine());
        var completed = await Task.WhenAny(readTask, Task.Delay(3000));
        return completed == readTask && readTask.Status == TaskStatus.RanToCompletion
            ? readTask.Result
            : null;
    }
}
