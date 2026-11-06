using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LanFlow.Desktop.Ocr;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LanFlow.Ocr;

/// <summary>
/// PaddleOCR PP-OCRv5 mobile 推理实现：DBNet 文本检测 → 逐行 CRNN/CTC 识别。
/// 仅依赖 ONNX Runtime，图像预处理为纯 C#。
/// </summary>
public sealed class PaddleOcrEngine : IOcrEngine
{
    private const float DetThreshold = 0.30f;
    private const int DetMaxSide = 960;
    private const int RecHeight = 48;
    private const float DetUnclipRatio = 1.5f;

    private static readonly float[] DetMean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] DetStd = [0.229f, 0.224f, 0.225f];

    private InferenceSession? _det;
    private InferenceSession? _rec;
    private string[] _chars = [];
    private bool _disposed;

    public void Initialize(string modelDirectory)
    {
        var detPath = Path.Combine(modelDirectory, "ppocrv5_mobile_det.onnx");
        var recPath = Path.Combine(modelDirectory, "ppocrv5_mobile_rec.onnx");
        var keysPath = Path.Combine(modelDirectory, "ppocr_keys.txt");

        if (!File.Exists(detPath)) throw new FileNotFoundException("缺少检测模型", detPath);
        if (!File.Exists(recPath)) throw new FileNotFoundException("缺少识别模型", recPath);
        if (!File.Exists(keysPath)) throw new FileNotFoundException("缺少识别字典", keysPath);

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount - 1, 1, 4),
            InterOpNumThreads = 1,
            EnableCpuMemArena = true,
        };

        _det = new InferenceSession(detPath, options);
        _rec = new InferenceSession(recPath, options);

        // PaddleOCR 字典：文件每行一个字符；模型输出为「blank + 字典 + 空格」。
        var keys = File.ReadAllLines(keysPath, Encoding.UTF8)
            .Select(l => l.TrimEnd('\r', '\n'))
            .Where(l => l.Length > 0)
            .ToList();
        keys.Add(" "); // 与 use_space_char=True 对齐
        _chars = keys.ToArray();
    }

    public OcrResult Recognize(byte[] bgraPixels, int pixelWidth, int pixelHeight, int stride)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_det is null || _rec is null) throw new InvalidOperationException("OCR 引擎尚未初始化");
        if (pixelWidth <= 0 || pixelHeight <= 0) return new OcrResult([]);

        var boxes = Detect(bgraPixels, stride, pixelWidth, pixelHeight);
        if (boxes.Count == 0) return new OcrResult([]);

        var lines = new List<OcrLine>(boxes.Count);
        foreach (var box in OrderBoxes(boxes))
        {
            var text = RecognizeLine(bgraPixels, stride, pixelWidth, pixelHeight, box, out var confidence);
            if (text.Length > 0)
            {
                lines.Add(new OcrLine(text, confidence, box.Left, box.Top, box.Width, box.Height));
            }
        }

        return new OcrResult(lines);
    }

    // ---------- 检测 ----------

    private sealed class Box
    {
        public float Left;
        public float Top;
        public float Right;
        public float Bottom;

        public float Width => Right - Left;
        public float Height => Bottom - Top;
        public float CenterX => (Left + Right) / 2f;
        public float CenterY => (Top + Bottom) / 2f;
    }

    private List<Box> Detect(byte[] bgra, int stride, int width, int height)
    {
        // 长边限制到 960，并向上取整到 32 的整数倍。
        var scale = Math.Min(1.0, (double)DetMaxSide / Math.Max(width, height));
        var targetWidth = Math.Max(32, (int)Math.Ceiling(width * scale / 32.0) * 32);
        var targetHeight = Math.Max(32, (int)Math.Ceiling(height * scale / 32.0) * 32);

        var tensor = Preprocess.ResizeToTensor(bgra, stride, width, height, targetWidth, targetHeight, DetMean, DetStd);
        var input = new DenseTensor<float>(tensor, [1, 3, targetHeight, targetWidth]);

        var inputName = _det!.InputMetadata.Keys.First();
        using var results = _det.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, input) });
        var output = results.First().AsTensor<float>();
        var dims = output.Dimensions;

        var outHeight = dims[^2];
        var outWidth = dims[^1];
        var source = output.ToArray();

        var mask = new byte[outWidth * outHeight];
        for (var i = 0; i < mask.Length; i++)
        {
            mask[i] = source[i] >= DetThreshold ? (byte)1 : (byte)0;
        }

        var components = FindComponents(mask, outWidth, outHeight);
        var boxes = MergeComponents(components, outWidth, outHeight);

        // 映射回原图坐标并外扩留白（供识别模型裁切）。
        var scaleX = width / (float)outWidth;
        var scaleY = height / (float)outHeight;
        var mapped = new List<Box>(boxes.Count);

        foreach (var b in boxes)
        {
            var padX = Math.Max(2f, b.Height * 0.18f);
            var padY = Math.Max(3f, b.Height * 0.25f);

            var left = Math.Max(0f, (b.Left - padX) * scaleX);
            var top = Math.Max(0f, (b.Top - padY) * scaleY);
            var right = Math.Min(width, (b.Right + padX) * scaleX);
            var bottom = Math.Min(height, (b.Bottom + padY) * scaleY);

            if (right - left < 6 || bottom - top < 6) continue;
            mapped.Add(new Box { Left = left, Top = top, Right = right, Bottom = bottom });
        }

        return mapped;
    }

    /// <summary>
    /// 8 邻域连通域标记，返回每个连通块的外接框（概率图坐标系）。
    /// </summary>
    private static List<Box> FindComponents(byte[] mask, int width, int height)
    {
        var result = new List<Box>();
        var visited = new byte[mask.Length];
        var stack = new int[width * height];
        var minCount = 6;

        for (var start = 0; start < mask.Length; start++)
        {
            if (mask[start] == 0 || visited[start] != 0) continue;

            var top = 0;
            stack[top++] = start;
            visited[start] = 1;

            var left = width;
            var right = 0;
            var minY = height;
            var maxY = 0;
            var count = 0;

            while (top > 0)
            {
                var index = stack[--top];
                var x = index % width;
                var y = index / width;
                count++;

                if (x < left) left = x;
                if (x > right) right = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                for (var dy = -1; dy <= 1; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= height) continue;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        if (nx < 0 || nx >= width) continue;
                        var ni = ny * width + nx;
                        if (mask[ni] == 0 || visited[ni] != 0) continue;
                        visited[ni] = 1;
                        stack[top++] = ni;
                    }
                }
            }

            if (count < minCount) continue;
            var boxHeight = maxY - minY + 1;
            if (boxHeight < 3) continue;

            result.Add(new Box
            {
                Left = left,
                Top = minY,
                Right = right + 1,
                Bottom = maxY + 1,
            });
        }

        return result;
    }

    /// <summary>
    /// 将字符级连通块按「同一行 + 水平邻近」合并为文本行框。截图场景以横排文本为主，无需完整轮廓追踪。
    /// </summary>
    private static List<Box> MergeComponents(List<Box> components, int width, int height)
    {
        if (components.Count == 0) return [];

        var parent = Enumerable.Range(0, components.Count).ToArray();

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[rb] = ra;
        }

        for (var i = 0; i < components.Count; i++)
        {
            var a = components[i];
            for (var j = i + 1; j < components.Count; j++)
            {
                var b = components[j];

                var minHeight = Math.Min(a.Height, b.Height);
                if (Math.Abs(a.CenterY - b.CenterY) > minHeight * 0.65f) continue;

                var overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
                if (overlap < minHeight * 0.45f) continue;

                var gap = Math.Max(a.Left, b.Left) - Math.Min(a.Right, b.Right);
                if (gap > minHeight * 0.9f) continue;

                Union(i, j);
            }
        }

        var groups = new Dictionary<int, Box>();
        for (var i = 0; i < components.Count; i++)
        {
            var root = Find(i);
            var c = components[i];
            if (!groups.TryGetValue(root, out var box))
            {
                groups[root] = new Box { Left = c.Left, Top = c.Top, Right = c.Right, Bottom = c.Bottom };
                continue;
            }

            box.Left = Math.Min(box.Left, c.Left);
            box.Top = Math.Min(box.Top, c.Top);
            box.Right = Math.Max(box.Right, c.Right);
            box.Bottom = Math.Max(box.Bottom, c.Bottom);
        }

        return groups.Values
            .Where(b => b.Width >= 3 && b.Height >= 3)
            .Where(b => b.Width < width * 0.98 || b.Height < height * 0.98)
            .ToList();
    }

    /// <summary>
    /// 阅读顺序：先按行（垂直中心接近视为同一行），行内从左到右。
    /// </summary>
    private static List<Box> OrderBoxes(List<Box> boxes)
    {
        var ordered = new List<Box>(boxes.Count);
        var remaining = boxes.OrderBy(b => b.Top).ToList();

        while (remaining.Count > 0)
        {
            var first = remaining[0];
            var row = remaining.Where(b => Math.Abs(b.CenterY - first.CenterY) <= Math.Min(b.Height, first.Height) * 0.6f).ToList();
            ordered.AddRange(row.OrderBy(b => b.Left));
            remaining = remaining.Except(row).ToList();
        }

        return ordered;
    }

    // ---------- 识别 ----------

    private string RecognizeLine(byte[] bgra, int stride, int width, int height, Box box, out float confidence)
    {
        confidence = 0f;

        var left = Math.Max(0, (int)Math.Floor(box.Left));
        var top = Math.Max(0, (int)Math.Floor(box.Top));
        var right = Math.Min(width, (int)Math.Ceiling(box.Right));
        var bottom = Math.Min(height, (int)Math.Ceiling(box.Bottom));
        var cropWidth = right - left;
        var cropHeight = bottom - top;
        if (cropWidth <= 0 || cropHeight <= 0) return string.Empty;

        var tensor = Preprocess.CropToTensor(bgra, stride, width, height, left, top, cropWidth, cropHeight, RecHeight);
        var targetWidth = tensor.Length / (3 * RecHeight);
        var input = new DenseTensor<float>(tensor, [1, 3, RecHeight, targetWidth]);

        var inputName = _rec!.InputMetadata.Keys.First();
        using var results = _rec.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, input) });
        var output = results.First().AsTensor<float>();
        var dims = output.Dimensions;
        if (dims.Length != 3) return string.Empty;

        var steps = dims[1];
        var classes = dims[2];
        var data = output.ToArray();

        var builder = new StringBuilder();
        var kept = new List<int>();
        var previous = -1;

        for (var t = 0; t < steps; t++)
        {
            var offset = t * classes;
            var best = 0;
            var bestValue = data[offset];
            for (var c = 1; c < classes; c++)
            {
                var v = data[offset + c];
                if (v > bestValue)
                {
                    bestValue = v;
                    best = c;
                }
            }

            if (best == 0 || best == previous) { previous = best; continue; }
            previous = best;
            kept.Add(t);

            var charIndex = best - 1;
            if (charIndex >= 0 && charIndex < _chars.Length)
            {
                builder.Append(_chars[charIndex]);
            }
        }

        if (kept.Count > 0)
        {
            // PaddleOCR 的 rec 模型输出已是 softmax 概率，直接取峰值概率的平均值。
            var total = 0.0;
            foreach (var t in kept)
            {
                var offset = t * classes;
                var max = data[offset];
                for (var c = 1; c < classes; c++)
                {
                    if (data[offset + c] > max) max = data[offset + c];
                }
                total += max;
            }
            confidence = (float)(total / kept.Count);
        }

        return builder.ToString().Trim();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _det?.Dispose();
        _rec?.Dispose();
        _det = null;
        _rec = null;
    }
}
