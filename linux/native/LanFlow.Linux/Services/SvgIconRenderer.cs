using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace LanFlow.Desktop.Services;

/// <summary>
/// B6-2：轻量 SVG 渲染器——解决 UOS/Deepin 系统图标（bloom 等主题）以 SVG 为主、
/// 而 Avalonia.Svg.Skia 会把 SkiaSharp 抬到 3.116 与 glibc 2.28 冲突（D8）导致图标全灭的问题。
/// 本方案用项目已钉住的 SkiaSharp 2.88.9 直接解析 SVG 子集（path/rect/circle/ellipse/line/polygon/polyline
/// + fill/stroke），零新增依赖、不碰 glibc。高级特性（渐变/pattern/text/transform）不支持时该图标回退占位。
/// </summary>
internal static class SvgIconRenderer
{
    private static readonly Regex ElementRegex = new(
        @"<(?<tag>path|rect|circle|ellipse|line|polygon|polyline)\b(?<attrs>[^>]*)>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ViewBoxRegex = new(
        @"viewBox\s*=\s*[""'](?<vb>[-\d.\s]+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IImage? Render(string path, double size)
    {
        try
        {
            var text = File.ReadAllText(path);
            var shapes = ParseShapes(text);
            if (shapes.Count == 0)
            {
                return null;
            }

            var viewBox = ParseViewBox(text);

            using var surface = SKSurface.Create(new SKImageInfo((int)size, (int)size, SKColorType.Rgba8888));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            // viewBox -> 画布等比缩放并居中
            double scale;
            if (viewBox.Width > 0 && viewBox.Height > 0)
            {
                scale = Math.Min(size / viewBox.Width, size / viewBox.Height);
                canvas.Translate((float)((size - viewBox.Width * scale) / 2 - viewBox.X * scale),
                                 (float)((size - viewBox.Height * scale) / 2 - viewBox.Y * scale));
                canvas.Scale((float)scale);
            }

            foreach (var shape in shapes)
            {
                using var skPath = BuildPath(shape);
                if (skPath is null)
                {
                    continue;
                }

                using var paint = new SKPaint { IsAntialias = true };
                var fill = ParseColor(shape.Fill);
                var stroke = ParseColor(shape.Stroke);
                if (fill is not null)
                {
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = fill.Value;
                }
                else if (stroke is not null)
                {
                    paint.Style = SKPaintStyle.Stroke;
                    paint.Color = stroke.Value;
                    paint.StrokeWidth = (float)(shape.StrokeWidth > 0 ? shape.StrokeWidth : 1);
                    paint.StrokeCap = SKStrokeCap.Round;
                    paint.StrokeJoin = SKStrokeJoin.Round;
                }
                else
                {
                    // 无 fill/stroke 时按 SVG 默认 fill=black
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = SKColors.Black;
                }

                canvas.DrawPath(skPath, paint);
            }

            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(data.ToArray());
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    private static (double X, double Y, double Width, double Height) ParseViewBox(string text)
    {
        var match = ViewBoxRegex.Match(text);
        if (!match.Success)
        {
            return (0, 0, 0, 0);
        }

        var parts = match.Groups["vb"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 ||
            !double.TryParse(parts[0], out var x) || !double.TryParse(parts[1], out var y) ||
            !double.TryParse(parts[2], out var w) || !double.TryParse(parts[3], out var h))
        {
            return (0, 0, 0, 0);
        }

        return (x, y, w, h);
    }

    private static List<SvgShape> ParseShapes(string text)
    {
        var result = new List<SvgShape>();
        foreach (Match match in ElementRegex.Matches(text))
        {
            var tag = match.Groups["tag"].Value.ToLowerInvariant();
            var attrs = ParseAttributes(match.Groups["attrs"].Value);
            var shape = new SvgShape { Tag = tag };

            attrs.TryGetValue("d", out var d);
            shape.Data = d;
            attrs.TryGetValue("fill", out var fill);
            shape.Fill = NormalizeColorToken(fill);
            attrs.TryGetValue("stroke", out var stroke);
            shape.Stroke = NormalizeColorToken(stroke);
            if (attrs.TryGetValue("stroke-width", out var sw) && double.TryParse(sw, out var width))
            {
                shape.StrokeWidth = width;
            }

            // 几何元素坐标
            if (attrs.TryGetValue("x", out var sx) && double.TryParse(sx, out var x)) shape.X = x;
            if (attrs.TryGetValue("y", out var sy) && double.TryParse(sy, out var y)) shape.Y = y;
            if (attrs.TryGetValue("cx", out var cx) && double.TryParse(cx, out var cxv)) shape.Cx = cxv;
            if (attrs.TryGetValue("cy", out var cy) && double.TryParse(cy, out var cyv)) shape.Cy = cyv;
            if (attrs.TryGetValue("r", out var r) && double.TryParse(r, out var rv)) shape.R = rv;
            if (attrs.TryGetValue("rx", out var rx) && double.TryParse(rx, out var rxv)) shape.Rx = rxv;
            if (attrs.TryGetValue("ry", out var ry) && double.TryParse(ry, out var ryv)) shape.Ry = ryv;
            if (attrs.TryGetValue("width", out var w) && double.TryParse(w, out var wv)) shape.Width = wv;
            if (attrs.TryGetValue("height", out var h) && double.TryParse(h, out var hv)) shape.Height = hv;
            if (attrs.TryGetValue("points", out var pts)) shape.Points = pts;

            result.Add(shape);
        }

        return result;
    }

    private static Dictionary<string, string> ParseAttributes(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(raw, @"(?<key>[\w:-]+)\s*=\s*[""'](?<value>[^""']*)[""']");
        foreach (Match match in matches)
        {
            result[match.Groups["key"].Value] = match.Groups["value"].Value.Trim();
        }

        return result;
    }

    private static string? NormalizeColorToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // 渐变/图案引用不支持，回退 null（该形状不绘制）
        if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }

    private static SKColor? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.StartsWith('#'))
        {
            var hex = value.TrimStart('#');
            if (hex.Length == 3)
            {
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2], "FF");
            }
            else if (hex.Length == 6)
            {
                hex += "FF";
            }

            if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var argb))
            {
                return new SKColor((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
            }

            return null;
        }

        return value.ToLowerInvariant() switch
        {
            "black" => SKColors.Black,
            "white" => SKColors.White,
            "red" => SKColors.Red,
            "green" => SKColors.Green,
            "blue" => SKColors.Blue,
            "gray" or "grey" => SKColors.Gray,
            "currentcolor" => SKColors.Black,
            "transparent" => new SKColor(0, 0, 0, 0),
            _ => null,
        };
    }

    private static SKPath? BuildPath(SvgShape shape)
    {
        try
        {
            var path = new SKPath();
            switch (shape.Tag)
            {
                case "path":
                    if (string.IsNullOrWhiteSpace(shape.Data) || !ParsePathData(path, shape.Data!))
                    {
                        return null;
                    }

                    break;
                case "rect":
                    path.AddRect(SKRect.Create((float)shape.X, (float)shape.Y, (float)shape.Width, (float)shape.Height));
                    break;
                case "circle":
                    path.AddCircle((float)shape.Cx, (float)shape.Cy, (float)shape.R);
                    break;
                case "ellipse":
                    path.AddOval(SKRect.Create((float)(shape.Cx - shape.Rx), (float)(shape.Cy - shape.Ry), (float)(shape.Rx * 2), (float)(shape.Ry * 2)));
                    break;
                case "line":
                    // line 元素无坐标解析（x1/y1/x2/y2），极少见，跳过
                    return null;
                case "polygon":
                case "polyline":
                    if (!ParsePoints(path, shape.Points, shape.Tag == "polygon"))
                    {
                        return null;
                    }

                    break;
                default:
                    return null;
            }

            return path;
        }
        catch
        {
            return null;
        }
    }

    private static bool ParsePoints(SKPath path, string? points, bool close)
    {
        if (string.IsNullOrWhiteSpace(points))
        {
            return false;
        }

        var tokens = Regex.Split(points.Trim(), @"[\s,]+");
        if (tokens.Length < 2)
        {
            return false;
        }

        var started = false;
        for (var i = 0; i + 1 < tokens.Length; i += 2)
        {
            if (!double.TryParse(tokens[i], out var x) || !double.TryParse(tokens[i + 1], out var y))
            {
                return false;
            }

            if (!started)
            {
                path.MoveTo((float)x, (float)y);
                started = true;
            }
            else
            {
                path.LineTo((float)x, (float)y);
            }
        }

        if (close)
        {
            path.Close();
        }

        return started;
    }

    /// <summary>SVG path d 解析：支持 M/m L/l H/h V/v C/c S/s Q/q T/t Z/z（A 椭圆弧用直线近似）。</summary>
    private static bool ParsePathData(SKPath path, string d)
    {
        var tokens = Tokenize(d);
        var index = 0;
        var x = 0.0;
        var y = 0.0;
        var startX = 0.0;
        var startY = 0.0;
        var lastCmd = ' ';
        var controlX = 0.0;
        var controlY = 0.0;
        var subStart = true; // 新子路径首点后 need moveto

        while (index < tokens.Count)
        {
            var token = tokens[index];
            char cmd;
            if (IsCommand(token))
            {
                cmd = token[0];
                index++;
            }
            else
            {
                if (lastCmd is 'M' or 'm')
                {
                    cmd = lastCmd == 'M' ? 'L' : 'l';
                }
                else if (lastCmd is 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'C' or 'c' or 'S' or 's' or 'Q' or 'q' or 'T' or 't' or 'Z' or 'z')
                {
                    cmd = lastCmd;
                }
                else
                {
                    return false;
                }
            }

            var relative = char.IsLower(cmd);
            var upper = char.ToUpperInvariant(cmd);
            switch (upper)
            {
                case 'M':
                    if (!NextPair(tokens, ref index, out var mx, out var my)) return false;
                    if (relative) { mx += x; my += y; }
                    path.MoveTo((float)mx, (float)my);
                    x = mx; y = my; startX = x; startY = y;
                    subStart = false;
                    break;
                case 'L':
                    if (!NextPair(tokens, ref index, out var lx, out var ly)) return false;
                    if (relative) { lx += x; ly += y; }
                    path.LineTo((float)lx, (float)ly);
                    x = lx; y = ly;
                    break;
                case 'H':
                    if (index >= tokens.Count) return false;
                    var hx = ParseNumber(tokens[index++]);
                    if (relative) hx += x;
                    path.LineTo((float)hx, (float)y);
                    x = hx;
                    break;
                case 'V':
                    if (index >= tokens.Count) return false;
                    var vy = ParseNumber(tokens[index++]);
                    if (relative) vy += y;
                    path.LineTo((float)x, (float)vy);
                    y = vy;
                    break;
                case 'C':
                    if (!NextPair(tokens, ref index, out var c1x, out var c1y)) return false;
                    if (!NextPair(tokens, ref index, out var c2x, out var c2y)) return false;
                    if (!NextPair(tokens, ref index, out var cex, out var cey)) return false;
                    if (relative) { c1x += x; c1y += y; c2x += x; c2y += y; cex += x; cey += y; }
                    path.CubicTo((float)c1x, (float)c1y, (float)c2x, (float)c2y, (float)cex, (float)cey);
                    controlX = c2x; controlY = c2y;
                    x = cex; y = cey;
                    break;
                case 'S':
                    if (!NextPair(tokens, ref index, out var s2x, out var s2y)) return false;
                    if (!NextPair(tokens, ref index, out var sex, out var sey)) return false;
                    if (relative) { s2x += x; s2y += y; sex += x; sey += y; }
                    var s1x = (lastCmd is 'C' or 'c' or 'S' or 's') ? 2 * x - controlX : x;
                    var s1y = (lastCmd is 'C' or 'c' or 'S' or 's') ? 2 * y - controlY : y;
                    path.CubicTo((float)s1x, (float)s1y, (float)s2x, (float)s2y, (float)sex, (float)sey);
                    controlX = s2x; controlY = s2y;
                    x = sex; y = sey;
                    break;
                case 'Q':
                    if (!NextPair(tokens, ref index, out var qx, out var qy)) return false;
                    if (!NextPair(tokens, ref index, out var qex, out var qey)) return false;
                    if (relative) { qx += x; qy += y; qex += x; qey += y; }
                    path.QuadTo((float)qx, (float)qy, (float)qex, (float)qey);
                    controlX = qx; controlY = qy;
                    x = qex; y = qey;
                    break;
                case 'T':
                    if (!NextPair(tokens, ref index, out var tex, out var tey)) return false;
                    if (relative) { tex += x; tey += y; }
                    var t1x = (lastCmd is 'Q' or 'q' or 'T' or 't') ? 2 * x - controlX : x;
                    var t1y = (lastCmd is 'Q' or 'q' or 'T' or 't') ? 2 * y - controlY : y;
                    path.QuadTo((float)t1x, (float)t1y, (float)tex, (float)tey);
                    controlX = t1x; controlY = t1y;
                    x = tex; y = tey;
                    break;
                case 'A':
                    // 椭圆弧：读取参数但用直线近似（UOS 图标 arc 少见，保底可显示轮廓）
                    if (index + 6 >= tokens.Count) return false;
                    index += 5; // rx ry rot large sweep 跳过
                    if (!NextPair(tokens, ref index, out var ax, out var ay)) return false;
                    if (relative) { ax += x; ay += y; }
                    path.LineTo((float)ax, (float)ay);
                    x = ax; y = ay;
                    break;
                case 'Z':
                    path.Close();
                    x = startX; y = startY;
                    break;
                default:
                    return false;
            }

            lastCmd = cmd;
            _ = subStart;
        }

        return true;
    }

    private static List<string> Tokenize(string d)
    {
        var tokens = new List<string>();
        var buffer = new System.Text.StringBuilder();
        for (var i = 0; i < d.Length; i++)
        {
            var c = d[i];
            if (char.IsLetter(c) || c == '+' || c == '-')
            {
                if (buffer.Length > 0)
                {
                    tokens.Add(buffer.ToString());
                    buffer.Clear();
                }

                if (char.IsLetter(c))
                {
                    tokens.Add(c.ToString());
                }
                else
                {
                    buffer.Append(c); // 正负号开始一个数字
                }

                continue;
            }

            if (c == '.')
            {
                buffer.Append(c);
                continue;
            }

            if (char.IsDigit(c))
            {
                buffer.Append(c);
                continue;
            }

            if (c is ' ' or ',' or '\t' or '\r' or '\n' or 'e' or 'E')
            {
                // e/E 指数符号：与后续数字同属一个数字
                if (c is 'e' or 'E')
                {
                    buffer.Append(c);
                }
                else if (buffer.Length > 0)
                {
                    tokens.Add(buffer.ToString());
                    buffer.Clear();
                }
            }
        }

        if (buffer.Length > 0)
        {
            tokens.Add(buffer.ToString());
        }

        return tokens;
    }

    private static bool IsCommand(string token) => token.Length == 1 && char.IsLetter(token[0]);

    private static bool NextPair(List<string> tokens, ref int index, out double x, out double y)
    {
        if (index + 1 >= tokens.Count)
        {
            x = 0;
            y = 0;
            return false;
        }

        x = ParseNumber(tokens[index]);
        y = ParseNumber(tokens[index + 1]);
        index += 2;
        return true;
    }

    private static double ParseNumber(string token)
    {
        // Tokenize 已处理 e/E，这里统一替换再解析
        return double.TryParse(token, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private sealed class SvgShape
    {
        public string Tag = string.Empty;
        public string? Data;
        public string? Fill;
        public string? Stroke;
        public double StrokeWidth;
        public double X;
        public double Y;
        public double Cx;
        public double Cy;
        public double R;
        public double Rx;
        public double Ry;
        public double Width;
        public double Height;
        public string? Points;
    }
}
