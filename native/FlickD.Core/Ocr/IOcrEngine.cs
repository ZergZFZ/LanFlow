namespace FlickD.Desktop.Ocr;

/// <summary>
/// 单行识别结果。坐标为原图像素坐标（左上角原点）。
/// </summary>
public sealed record OcrLine(string Text, double Confidence, double X, double Y, double Width, double Height);

/// <summary>
/// 识别结果：保留逐行信息，同时提供整段文本。
/// </summary>
public sealed class OcrResult
{
    public OcrResult(IReadOnlyList<OcrLine> lines)
    {
        Lines = lines;
        FullText = string.Join("\n", lines.Select(l => l.Text));
    }

    public IReadOnlyList<OcrLine> Lines { get; }

    public string FullText { get; }
}

/// <summary>
/// OCR 引擎契约。宿主通过插件加载实现，本身不依赖 WPF。
/// 实现必须允许在后台线程调用 <see cref="Recognize"/>。
/// </summary>
public interface IOcrEngine : IDisposable
{
    /// <summary>
    /// 加载模型。modelDirectory 为包含 onnx 模型与字典的目录。
    /// </summary>
    void Initialize(string modelDirectory);

    /// <summary>
    /// 识别 BGRA 像素（<paramref name="stride"/> 为一行字节数）。
    /// </summary>
    OcrResult Recognize(byte[] bgraPixels, int pixelWidth, int pixelHeight, int stride);
}
