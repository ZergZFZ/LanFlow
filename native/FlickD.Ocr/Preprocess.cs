using System;

namespace FlickD.Ocr;

/// <summary>
/// 纯 C# 图像预处理：BGRA 源图 → 双线性缩放 → RGB 归一化 → NCHW float 张量。
/// 不引入 OpenCV，避免额外原生依赖。
/// </summary>
internal static class Preprocess
{
    /// <summary>
    /// 缩放并归一化。等比例缩放到目标框内，剩余区域用归一化后的 0（即均值色）填充。
    /// </summary>
    public static float[] ResizeToTensor(
        byte[] bgra, int stride, int width, int height,
        int targetWidth, int targetHeight,
        float[] mean, float[] std)
    {
        var tensor = new float[3 * targetWidth * targetHeight];
        var plane = targetWidth * targetHeight;

        // 与 PaddleOCR DetResizeForTest 一致：左上角对齐，右下补零。
        var scale = Math.Min((double)targetWidth / width, (double)targetHeight / height);
        var drawWidth = Math.Max(1, (int)Math.Round(width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(height * scale));

        const int offsetX = 0;
        const int offsetY = 0;

        for (var dy = 0; dy < drawHeight; dy++)
        {
            var srcY = Math.Min(height - 1, (int)((dy + 0.5) / scale));
            var rowBase = srcY * stride;

            for (var dx = 0; dx < drawWidth; dx++)
            {
                var srcX = Math.Min(width - 1, (int)((dx + 0.5) / scale));
                var p = rowBase + srcX * 4;
                var b = bgra[p] / 255f;
                var g = bgra[p + 1] / 255f;
                var r = bgra[p + 2] / 255f;

                var index = (offsetY + dy) * targetWidth + (offsetX + dx);
                tensor[index] = (r - mean[0]) / std[0];
                tensor[plane + index] = (g - mean[1]) / std[1];
                tensor[2 * plane + index] = (b - mean[2]) / std[2];
            }
        }

        return tensor;
    }

    /// <summary>
    /// 从源图裁剪并缩放到固定高度（保持宽高比），归一化到 [-1,1] 的 NCHW 张量。
    /// </summary>
    public static float[] CropToTensor(
        byte[] bgra, int stride, int width, int height,
        int left, int top, int cropWidth, int cropHeight,
        int targetHeight)
    {
        var targetWidth = Math.Max(8, (int)Math.Ceiling(targetHeight * (double)cropWidth / cropHeight));
        if (targetWidth > 3200) targetWidth = 3200;
        // 宽度按 8 对齐，便于推理内核处理。
        targetWidth = (targetWidth + 7) / 8 * 8;

        var tensor = new float[3 * targetWidth * targetHeight];
        var plane = targetWidth * targetHeight;

        // 双线性采样，避免小字号上采样时的锯齿导致识别退化。
        for (var dy = 0; dy < targetHeight; dy++)
        {
            var fy = top + (dy + 0.5) * cropHeight / targetHeight - 0.5;
            var y0 = Math.Clamp((int)Math.Floor(fy), 0, height - 1);
            var y1 = Math.Min(y0 + 1, height - 1);
            var wy = Math.Clamp(fy - Math.Floor(fy), 0, 1);

            for (var dx = 0; dx < targetWidth; dx++)
            {
                var fx = left + (dx + 0.5) * cropWidth / targetWidth - 0.5;
                var x0 = Math.Clamp((int)Math.Floor(fx), 0, width - 1);
                var x1 = Math.Min(x0 + 1, width - 1);
                var wx = Math.Clamp(fx - Math.Floor(fx), 0, 1);

                var index = dy * targetWidth + dx;
                tensor[index] = SampleChannel(bgra, stride, x0, y0, x1, y1, wx, wy, 2);
                tensor[plane + index] = SampleChannel(bgra, stride, x0, y0, x1, y1, wx, wy, 1);
                tensor[2 * plane + index] = SampleChannel(bgra, stride, x0, y0, x1, y1, wx, wy, 0);
            }
        }

        return tensor;
    }

    private static float SampleChannel(
        byte[] bgra, int stride, int x0, int y0, int x1, int y1, double wx, double wy, int offset)
    {
        var topLeft = bgra[y0 * stride + x0 * 4 + offset];
        var topRight = bgra[y0 * stride + x1 * 4 + offset];
        var bottomLeft = bgra[y1 * stride + x0 * 4 + offset];
        var bottomRight = bgra[y1 * stride + x1 * 4 + offset];

        var top = topLeft + (topRight - topLeft) * wx;
        var bottom = bottomLeft + (bottomRight - bottomLeft) * wx;
        var value = top + (bottom - top) * wy;
        return (float)(value / 127.5 - 1.0);
    }
}
