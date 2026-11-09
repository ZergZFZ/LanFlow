using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FlickD.Desktop.Services;

/// <summary>
/// FlickD 单实例守卫：命名互斥量检测已有实例，
/// 命名管道向已有实例发送"显示主窗口"指令并等待确认应答。
/// 2.0.0 改名过渡期同时识别旧版 LanFlow 实例，避免新旧版本同时驻留。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\FlickD.SingleInstance";
    private const string PipeName = "FlickD.SingleInstance.Pipe";

    // 改名前的实例名：只用于过渡期识别，2.1.0 摘除。
    private const string LegacyMutexName = @"Local\LanFlow.SingleInstance";
    private const string LegacyPipeName = "LanFlow.SingleInstance.Pipe";
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromMilliseconds(800);

    private readonly string _mutexName;
    private readonly string _pipeName;
    private readonly bool _supportsLegacyInstance;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _responseTimeout;
    private readonly Action<Action> _invokeOnUi;
    private readonly Action<string> _log;

    private Mutex? _mutex;
    private CancellationTokenSource? _listenerCancellation;
    private Thread? _listenerThread;
    private Action? _onShowRequested;
    private bool _existingIsLegacy;
    private bool _disposed;

    public SingleInstanceGuard()
        : this(uiExecutor: null, log: null, connectTimeout: null, responseTimeout: null, nameSuffix: null)
    {
    }

    // 供测试注入：同步 UI 执行器、日志捕获、独立实例名与更短超时，避免依赖 WPF Application 与真实互斥量。
    internal SingleInstanceGuard(
        Action<Action>? uiExecutor,
        Action<string>? log,
        TimeSpan? connectTimeout,
        TimeSpan? responseTimeout,
        string? nameSuffix)
    {
        var suffix = string.IsNullOrEmpty(nameSuffix) ? string.Empty : "." + nameSuffix;
        _mutexName = MutexName + suffix;
        _pipeName = PipeNameFor(nameSuffix);
        // 测试注入独立实例名时不参与旧名检测，避免与真实运行中的旧版本互相干扰。
        _supportsLegacyInstance = string.IsNullOrEmpty(nameSuffix);
        _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
        _responseTimeout = responseTimeout ?? DefaultResponseTimeout;
        _invokeOnUi = uiExecutor ?? DefaultUiExecutor;
        _log = log ?? (message => App.WriteDiagnosticLog(message));
    }

    internal static string PipeNameFor(string? nameSuffix)
    {
        var suffix = string.IsNullOrEmpty(nameSuffix) ? string.Empty : "." + nameSuffix;
        return PipeName + suffix;
    }

    private static void DefaultUiExecutor(Action action)
    {
        Application.Current?.Dispatcher.Invoke(action, System.Windows.Threading.DispatcherPriority.Send);
    }

    /// <summary>
    /// 尝试成为主实例。成功（无已有实例）返回 true 并开始监听；
    /// 已有实例存在时返回 false，由调用方决定唤醒或退出。
    /// </summary>
    public bool TryAcquire(Action onShowRequested)
    {
        ArgumentNullException.ThrowIfNull(onShowRequested);

        // 过渡期：旧版本（LanFlow 1.6.x）仍可能驻留，先探测旧互斥量。
        // 命中时按旧管道唤醒，不再创建新互斥量，避免托盘/热键重复。
        if (_supportsLegacyInstance && TryOpenLegacyInstance())
        {
            _existingIsLegacy = true;
            _log("single-instance:legacy-instance-detected");
            return false;
        }

        if (Mutex.TryOpenExisting(_mutexName, out var existing))
        {
            existing.Dispose();
            return false;
        }

        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
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
            Name = "FlickD.SingleInstanceListener",
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
            // 旧实例只监听旧管道名，唤醒必须走对应通道。
            var pipeName = _existingIsLegacy ? LegacyPipeName : _pipeName;
            using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            client.Connect(_connectTimeout);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true,
            };
            writer.WriteLine("show");

            var response = ReadLineWithTimeout(client, _responseTimeout, out var timedOut);
            _log(timedOut
                ? "single-instance:show-response-timeout"
                : "single-instance:show-response=" + (response ?? "none"));
        }
        catch (Exception exception) when (exception is IOException
            or TimeoutException
            or UnauthorizedAccessException
            or ObjectDisposedException)
        {
            // 已有实例可能正在退出或监听器尚未就绪；记录后由调用方直接退出。
            _log("single-instance:show-failed=" + exception.GetType().Name);
        }
    }

    // 管道流不支持 ReadTimeout（赋值会抛 InvalidOperationException，1.5.3 曾因此让二次启动崩溃），
    // 改用任务等待实现读超时；超时后底层读取随连接释放自然结束。
    private static string? ReadLineWithTimeout(
        NamedPipeClientStream client,
        TimeSpan timeout,
        out bool timedOut)
    {
        timedOut = false;
        var readTask = Task.Run(() =>
            new StreamReader(client, new UTF8Encoding(false), leaveOpen: true).ReadLine());
        if (readTask.Wait(timeout))
        {
            return readTask.Status == TaskStatus.RanToCompletion ? readTask.Result : null;
        }

        timedOut = true;
        readTask.ContinueWith(
            task => _ = task.Exception,
            TaskContinuationOptions.OnlyOnFaulted);
        return null;
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
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                server.WaitForConnectionAsync(cancellationToken).GetAwaiter().GetResult();

                using var reader = new StreamReader(server, new UTF8Encoding(false), leaveOpen: true);
                var command = reader.ReadLine();
                if (!IsShowCommand(command))
                {
                    continue;
                }

                // 先在 UI 线程完成显示，再回执 ok，保证"ok"代表窗口确实已处理显示请求。
                _invokeOnUi(() => _onShowRequested?.Invoke());
                using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
                {
                    AutoFlush = true,
                };
                writer.WriteLine("ok");
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

    internal static bool IsShowCommand(string? command) =>
        string.Equals(command?.Trim(), "show", StringComparison.OrdinalIgnoreCase);

    /// <summary>探测改名前的旧实例是否仍在运行；权限差异等异常一律视为未命中。</summary>
    private static bool TryOpenLegacyInstance()
    {
        try
        {
            if (!Mutex.TryOpenExisting(LegacyMutexName, out var existing))
            {
                return false;
            }

            existing.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
