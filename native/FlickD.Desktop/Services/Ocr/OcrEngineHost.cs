using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlickD.Desktop.Ocr;

namespace FlickD.Desktop.Services.Ocr;

public sealed class OcrUnavailableException : Exception
{
    public OcrUnavailableException(string message) : base(message) { }
}

/// <summary>
/// OCR 引擎宿主：按需加载插件与原生库（AddDllDirectory + 预载），缓存引擎实例。
/// 启动阶段不触碰任何 OCR 资源；只有用户启用并触发取字时才进入这里。
/// </summary>
public sealed class OcrEngineHost
{
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;
    private const uint LoadLibrarySearchUserDirs = 0x00000400;

    /// <summary>单次取字的总超时（含首次引擎加载与推理）。</summary>
    public static readonly TimeSpan RecognizeTimeout = TimeSpan.FromSeconds(45);

    /// <summary>引擎加载阶段的阶段回调：isInitializing=true 表示正在加载模型（首次 1–3s）。</summary>
    public event Action<bool>? PhaseChanged;

    public static OcrEngineHost Instance { get; } = new();

    private readonly object _gate = new();
    private IOcrEngine? _engine;
    private OcrLoadContext? _context;
    private string? _loadedRoot;

    public bool IsLoaded
    {
        get
        {
            lock (_gate) return _engine is not null;
        }
    }

    /// <summary>
    /// 识别。像素格式转换/拷贝与推理均在线程池执行，调用线程（UI）只负责传参，
    /// 避免全屏大选区（4K ≈ 33 MB）的拷贝与转换阻塞截图窗口。
    /// 输入须为冻结的 BitmapSource（跨线程安全），由调用方保证。
    /// 总超时护栏：引擎加载死锁或模型损坏时不能让"正在识别"永远挂起（P2-1）。
    /// </summary>
    public async Task<OcrResult> RecognizeAsync(BitmapSource image)
    {
        var width = image.PixelWidth;
        var height = image.PixelHeight;

        var work = Task.Run(() =>
        {
            var stride = width * 4;
            var pixels = new byte[stride * height];

            // FormatConvertedBitmap 为 Freezable，可在后台线程创建；不冻结即可在本线程使用。
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(pixels, stride, 0);

            var engine = EnsureEngine();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = engine.Recognize(pixels, width, height, stride);
            stopwatch.Stop();
            App.WriteDiagnosticLog(
                $"ocr:infer;ms={stopwatch.ElapsedMilliseconds};width={width};height={height};lines={result.Lines.Count}");
            return result;
        });

        var completed = await Task.WhenAny(work, Task.Delay(RecognizeTimeout)).ConfigureAwait(true);
        if (completed != work)
        {
            throw new OcrUnavailableException("取字超时：识别组件长时间无响应，请重试或重新安装 OCR 组件");
        }

        return await work.ConfigureAwait(true);
    }

    private IOcrEngine EnsureEngine()
    {
        lock (_gate)
        {
            if (_engine is not null) return _engine;

            var location = OcrBundleLocator.Locate();
            if (location.Status != OcrBundleStatus.Installed)
            {
                throw new OcrUnavailableException(location.Status == OcrBundleStatus.Corrupted
                    ? "OCR 组件已损坏，请在设置中重新下载或导入组件包"
                    : "尚未安装 OCR 组件");
            }

            var runtimeDirectory = Path.Combine(location.RootDirectory, "runtime");
            var modelsDirectory = Path.Combine(location.RootDirectory, "models");

            PhaseChanged?.Invoke(true);
            var loadStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                PreloadNative(runtimeDirectory, Path.Combine(location.RootDirectory, "vcredist"));

                var context = new OcrLoadContext(runtimeDirectory);
                var pluginPath = Path.Combine(runtimeDirectory, "FlickD.Ocr.dll");
                if (!File.Exists(pluginPath)) throw new OcrUnavailableException("OCR 组件缺少 FlickD.Ocr.dll");

                var assembly = context.LoadFromAssemblyPath(pluginPath);
                var type = assembly.GetType("FlickD.Ocr.PaddleOcrEngine")
                           ?? throw new OcrUnavailableException("OCR 组件类型缺失");
                var instance = Activator.CreateInstance(type) as IOcrEngine
                               ?? throw new OcrUnavailableException("OCR 组件无法实例化");

                instance.Initialize(modelsDirectory);

                _context = context;
                _engine = instance;
                _loadedRoot = location.RootDirectory;
                return _engine;
            }
            finally
            {
                loadStopwatch.Stop();
                App.WriteDiagnosticLog(
                    $"ocr:engine-load;ms={loadStopwatch.ElapsedMilliseconds};root={location.RootDirectory}");
                PhaseChanged?.Invoke(false);
            }
        }
    }

    private static void PreloadNative(string runtimeDirectory, string vcDirectory)
    {
        SetDefaultDllDirectories(LoadLibrarySearchDefaultDirs | LoadLibrarySearchUserDirs);
        AddDllDirectory(runtimeDirectory);
        AddDllDirectory(vcDirectory);

        // 先 CRT，再 onnxruntime，最后 providers_shared。
        foreach (var name in new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "msvcp140_1.dll" })
        {
            TryLoad(Path.Combine(vcDirectory, name));
        }

        foreach (var name in new[] { "onnxruntime.dll", "onnxruntime_providers_shared.dll" })
        {
            TryLoad(Path.Combine(runtimeDirectory, name));
        }
    }

    private static void TryLoad(string path)
    {
        try
        {
            if (File.Exists(path)) NativeLibrary.Load(path);
        }
        catch
        {
            // 单文件缺失不阻断整体加载，交由后续会话创建给出明确错误。
        }
    }

    /// <summary>
    /// 卸载组件或重新安装后调用，丢弃旧引擎实例与 bundle 校验缓存。
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            try { _engine?.Dispose(); } catch { /* ignore */ }
            _engine = null;
            _context = null;
            _loadedRoot = null;
        }

        OcrBundleLocator.Invalidate();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string newDirectory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);
}
