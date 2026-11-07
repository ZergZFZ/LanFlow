using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Views;

/// <summary>标注矢量显示层：直接读取模型并在 OnRender 中绘制，无中间 UI 元素。</summary>
public sealed class AnnotationVisualHost : FrameworkElement
{
    public IReadOnlyList<AnnotationShape> Shapes { get; set; } = Array.Empty<AnnotationShape>();

    public AnnotationShape? Draft { get; set; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var shape in Shapes)
        {
            AnnotationRenderer.Draw(drawingContext, shape, pixelsPerDip);
        }
        if (Draft is not null)
        {
            AnnotationRenderer.Draw(drawingContext, Draft, pixelsPerDip);
        }
    }
}

/// <summary>把 AnnotationShape 绘制到 DrawingContext；屏幕预览与最终合成都调用这里。</summary>
internal static class AnnotationRenderer
{
    public static void Draw(DrawingContext dc, AnnotationShape shape, double pixelsPerDip)
    {
        var brush = new SolidColorBrush(shape.Color);
        var pen = new Pen(brush, shape.StrokeWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        switch (shape.Tool)
        {
            case AnnotationTool.Rect:
                dc.DrawRectangle(null, pen, shape.Bounds);
                break;

            case AnnotationTool.Ellipse:
                var rect = shape.Bounds;
                dc.DrawEllipse(null, pen, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2),
                    rect.Width / 2, rect.Height / 2);
                break;

            case AnnotationTool.Arrow:
                DrawArrow(dc, shape, brush, pen);
                break;

            case AnnotationTool.Pen:
                DrawPen(dc, shape, pen);
                break;

            case AnnotationTool.Mosaic:
                var fill = new SolidColorBrush(Color.FromArgb(0x22, shape.Color.R, shape.Color.G, shape.Color.B));
                dc.DrawRectangle(fill, new Pen(brush, 1), shape.Bounds);
                break;

            case AnnotationTool.Number:
                DrawNumber(dc, shape, brush, pixelsPerDip);
                break;

            case AnnotationTool.Text:
                DrawText(dc, shape, pixelsPerDip);
                break;
        }
    }

    private static void DrawArrow(DrawingContext dc, AnnotationShape shape, Brush brush, Pen pen)
    {
        var a = shape.A;
        var b = shape.B;
        dc.DrawLine(pen, a, b);

        const double headLength = 15;
        const double halfWidth = 6;
        var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
        var back = new Point(b.X - headLength * Math.Cos(angle), b.Y - headLength * Math.Sin(angle));
        var px = -Math.Sin(angle) * halfWidth;
        var py = Math.Cos(angle) * halfWidth;
        var p1 = new Point(back.X + px, back.Y + py);
        var p2 = new Point(back.X - px, back.Y - py);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(b, true, true);
            ctx.LineTo(p1, true, false);
            ctx.LineTo(p2, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private static void DrawPen(DrawingContext dc, AnnotationShape shape, Pen pen)
    {
        if (shape.Points.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(shape.Points[0], false, false);
            ctx.PolyLineTo(shape.Points.GetRange(1, shape.Points.Count - 1), true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawNumber(DrawingContext dc, AnnotationShape shape, Brush brush, double pixelsPerDip)
    {
        const double radius = 13;
        var center = shape.A;
        dc.DrawEllipse(brush, null, center, radius, radius);

        var digitBrush = IsLight(shape.Color)
            ? new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1A))
            : System.Windows.Media.Brushes.White;
        var text = new FormattedText(
            shape.Text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            11,
            digitBrush,
            pixelsPerDip);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static void DrawText(DrawingContext dc, AnnotationShape shape, double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(shape.Text)) return;

        var typeface = new Typeface("Microsoft YaHei UI");
        var fillBrush = new SolidColorBrush(shape.Color);

        // 直接绘制，不加阴影/描边：小字号下任何偏移绘制都会产生重影，让文字发虚。
        var text = new FormattedText(shape.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, 16, fillBrush, pixelsPerDip);

        dc.DrawText(text, shape.A);
    }

    private static bool IsLight(Color c) =>
        (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 150;
}

/// <summary>马赛克（像素块化）工具：对源图指定区域按块取均值色。</summary>
internal static class MosaicUtil
{
    public static System.Windows.Media.Imaging.BitmapSource Apply(
        System.Windows.Media.Imaging.BitmapSource source,
        IReadOnlyList<Rect> rects, int block)
    {
        var bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        foreach (var r in rects)
        {
            var x0 = Math.Max(0, (int)r.X);
            var y0 = Math.Max(0, (int)r.Y);
            var x1 = Math.Min(width, (int)Math.Ceiling(r.Right));
            var y1 = Math.Min(height, (int)Math.Ceiling(r.Bottom));
            if (x1 <= x0 || y1 <= y0) continue;

            for (var by = y0; by < y1; by += block)
            {
                for (var bx = x0; bx < x1; bx += block)
                {
                    var bye = Math.Min(by + block, y1);
                    var bxe = Math.Min(bx + block, x1);
                    long bs = 0, gs = 0, rs = 0, asum = 0;
                    var count = 0;
                    for (var y = by; y < bye; y++)
                    {
                        for (var x = bx; x < bxe; x++)
                        {
                            var i = y * stride + x * 4;
                            bs += pixels[i];
                            gs += pixels[i + 1];
                            rs += pixels[i + 2];
                            asum += pixels[i + 3];
                            count++;
                        }
                    }
                    if (count == 0) continue;
                    var b = (byte)(bs / count);
                    var g = (byte)(gs / count);
                    var red = (byte)(rs / count);
                    var a = (byte)(asum / count);
                    for (var y = by; y < bye; y++)
                    {
                        for (var x = bx; x < bxe; x++)
                        {
                            var i = y * stride + x * 4;
                            pixels[i] = b;
                            pixels[i + 1] = g;
                            pixels[i + 2] = red;
                            pixels[i + 3] = a;
                        }
                    }
                }
            }
        }

        var wb = new System.Windows.Media.Imaging.WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        wb.Freeze();
        return wb;
    }
}
