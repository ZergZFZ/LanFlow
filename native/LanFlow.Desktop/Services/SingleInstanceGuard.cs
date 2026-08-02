using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;

namespace LanFlow.Desktop.Services;

/// <summary>
/// LanFlow 单实例守卫：命名互斥量检测已有实例，
/// 命名管道向已有实例发送"显示主窗口"指令。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\LanFlow.SingleInstance";
    private const string PipeName = @"LanFlow.SingleInstance.Pipe";
    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromMilliseconds(500);

    private Mutex? _mutex;
    private CancellationTokenSource? _listenerCancellation;
    private Thread? _listenerThread;
    private Action? _onShowRequested;
    private bool _disposed;

    /// <summary>
    /// 尝试成为主实例。成功（无已有实例）返回 true 并开始监听；
    /// 已有实例存在时返回 false，由调用方决定唤醒或退出。
    /// </summary>
    public bool TryAcquire(Action onShowRequested)
    {
        ArgumentNullException.ThrowIfNull(onShowRequested);

        if (Mutex.TryOpenExisting(MutexName, out var existing))
        {
            existing.Dispose();
            return false;
        }

        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _onShowRequested = onShowRequested;
        _listenerCancellation = new CancellationTokenSource();
        _listenerThread = new Thread(() => ListenForShowRequest(_listenerCancellation.Token))
        {
            IsBackground = true,
            Name = "LanFlow.SingleInstanceListener",
        };
        _listenerThread.Start();
        return true;
    }

    /// <summary>
    /// 通知已有实例显示主窗口。连接失败不抛异常（调用方仍应退出避免双开）。
    /// </summary>
    public void NotifyExistingShow()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.None);
            client.Connect(PipeConnectTimeout);
            using var writer = new StreamWriter(client, new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
            writer.WriteLine("show");
        }
        catch (Exception exception) when (exception is IOException
            or TimeoutException
            or UnauthorizedAccessException)
        {
            // 已有实例可能正在退出；不阻塞，由调用方直接退出。
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listenerCancellation?.Cancel();
        _listenerCancellation?.Dispose();
        _listenerCancellation = null;
        _listenerThread = null;

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 当前线程不持有互斥量时释放会抛异常，忽略即可。
        }

        _mutex?.Dispose();
        _mutex = null;
        _onShowRequested = null;
    }

    private void ListenForShowRequest(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                server.WaitForConnection();

                using var reader = new StreamReader(server, new UTF8Encoding(false));
                var command = reader.ReadLine();
                if (string.Equals(command, "show", StringComparison.OrdinalIgnoreCase))
                {
                    Application.Current?.Dispatcher.Invoke(
                        () => _onShowRequested?.Invoke(),
                        System.Windows.Threading.DispatcherPriority.Send);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ObjectDisposedException)
            {
                // 单个连接失败后继续等待下一个连接。
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
