using FlickD.Desktop.Services.Ocr;
using Xunit;

namespace FlickD.Desktop.Tests;

public sealed class OcrTextNormalizerTests
{
    [Fact]
    public void Normalize_TrimsLinesAndDropsEmptyOnes()
    {
        var text = "  第一行  \n\n\n   第二行\t\n";
        var result = OcrTextNormalizer.Normalize(text);

        Assert.Equal("第一行\n\n第二行", result);
    }

    [Fact]
    public void Normalize_CollapsesConsecutiveBlankLines()
    {
        var text = "标题\n\n\n\n正文一\n\n正文二";
        var result = OcrTextNormalizer.Normalize(text);

        Assert.Equal("标题\n\n正文一\n\n正文二", result);
    }

    [Fact]
    public void Normalize_HandlesCrLfAndEmptyInput()
    {
        Assert.Equal("a\nb", OcrTextNormalizer.Normalize("a\r\nb"));
        Assert.Equal(string.Empty, OcrTextNormalizer.Normalize(null));
        Assert.Equal(string.Empty, OcrTextNormalizer.Normalize("   "));
    }
}
