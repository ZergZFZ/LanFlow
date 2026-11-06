using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LanFlow.Desktop.Services.Ocr;

/// <summary>
/// 识别结果规范化：去行尾空白、折叠空行、去除首尾空行。
/// </summary>
public static class OcrTextNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder();
        var pendingBlank = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (builder.Length > 0) pendingBlank = true;
                continue;
            }

            if (pendingBlank)
            {
                builder.Append("\n\n");
                pendingBlank = false;
            }
            else if (builder.Length > 0)
            {
                builder.Append('\n');
            }
            builder.Append(line);
        }

        return builder.ToString();
    }
}
