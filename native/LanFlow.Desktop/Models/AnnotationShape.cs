using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace LanFlow.Desktop.Models;

/// <summary>标注工具类型。顺序即快捷键 1-7 与工具条按钮顺序。</summary>
public enum AnnotationTool
{
    Rect = 0,
    Arrow = 1,
    Pen = 2,
    Mosaic = 3,
    Number = 4,
    Text = 5,
    Ellipse = 6,
}

/// <summary>
/// 一个标注对象。坐标全部为「相对选区左上角」的 DIP；
/// 颜色与线宽在创建时固化，后续切换调色板/线宽不会重绘已有标注。
/// </summary>
public sealed class AnnotationShape
{
    public AnnotationTool Tool { get; init; }

    /// <summary>起点：矩形/箭头/椭圆的对角点之一；画笔首点；序号/文字锚点。</summary>
    public Point A { get; set; }

    /// <summary>终点：矩形/箭头/椭圆的对角点；画笔为最后一点。</summary>
    public Point B { get; set; }

    /// <summary>画笔轨迹（仅 Pen 使用）。</summary>
    public List<Point> Points { get; } = [];

    public string Text { get; set; } = string.Empty;

    public Color Color { get; set; } = Colors.White;

    public double StrokeWidth { get; set; } = 2;

    /// <summary>标注占用的近似包围盒（DIP），用于退化检测与局部刷新。</summary>
    public Rect Bounds
    {
        get
        {
            switch (Tool)
            {
                case AnnotationTool.Number:
                    return new Rect(A.X - 13, A.Y - 13, 26, 26);
                case AnnotationTool.Text:
                    return new Rect(A.X - 4, A.Y - 4, Math.Max(80, Text.Length * 18), 34);
                case AnnotationTool.Pen:
                    if (Points.Count < 2) return new Rect(A, new Size(1, 1));
                    var minX = Points[0].X;
                    var minY = Points[0].Y;
                    var maxX = Points[0].X;
                    var maxY = Points[0].Y;
                    for (var i = 1; i < Points.Count; i++)
                    {
                        var p = Points[i];
                        if (p.X < minX) minX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y > maxY) maxY = p.Y;
                    }
                    return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
                default:
                    return new Rect(
                        Math.Min(A.X, B.X),
                        Math.Min(A.Y, B.Y),
                        Math.Max(1, Math.Abs(B.X - A.X)),
                        Math.Max(1, Math.Abs(B.Y - A.Y)));
            }
        }
    }

    /// <summary>选区原点变化时整体平移（local = screen - origin，原点左/上移则 local 增大）。</summary>
    public void Offset(Vector delta)
    {
        A = new Point(A.X + delta.X, A.Y + delta.Y);
        B = new Point(B.X + delta.X, B.Y + delta.Y);
        for (var i = 0; i < Points.Count; i++)
        {
            Points[i] = new Point(Points[i].X + delta.X, Points[i].Y + delta.Y);
        }
    }
}
