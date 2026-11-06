using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LanFlow.Desktop.Ocr;

namespace LanFlow.Desktop.Services.Ocr;

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
    /// 识别。BitmapSource 的像素拷贝在调用线程（UI）完成，推理在后台线程执行。
    /// </summary>
    public Task<OcrResult> RecognizeAsync(BitmapSource image)
    {
        var width = image.PixelWidth;
        var height = image.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(pixels, stride, 0);

        return Task.Run(() => EnsureEngine().Recognize(pixels, width, height, stride));
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

            PreloadNative(runtimeDirectory, Path.Combine(location.RootDirectory, "vcredist"));

            var context = new OcrLoadContext(runtimeDirectory);
            var pluginPath = Path.Combine(runtimeDirectory, "LanFlow.Ocr.dll");
            if (!File.Exists(pluginPath)) throw new OcrUnavailableException("OCR 组件缺少 LanFlow.Ocr.dll");

            var assembly = context.LoadFromAssemblyPath(pluginPath);
            var type = assembly.GetType("LanFlow.Ocr.PaddleOcrEngine")
                       ?? throw new OcrUnavailableException("OCR 组件类型缺失");
            var instance = Activator.CreateInstance(type) as IOcrEngine
                           ?? throw new OcrUnavailableException("OCR 组件无法实例化");

            instance.Initialize(modelsDirectory);

            _context = context;
            _engine = instance;
            _loadedRoot = location.RootDirectory;
            return _engine;
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
    /// 卸载组件或重新安装后调用，丢弃旧引擎实例。
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
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string newDirectory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);
}
