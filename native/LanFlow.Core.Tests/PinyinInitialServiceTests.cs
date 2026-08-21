using LanFlow.Desktop.Services;

namespace LanFlow.Core.Tests;

public sealed class PinyinInitialServiceTests
{
    [Theory]
    [InlineData("微信", "WX")]
    [InlineData("幻兽帕鲁", "HSPL")]
    // 「浏」属 GB2312 二级字库（0xE4AF 超出一级区间），按设计被跳过：浏览器 → LQ
    [InlineData("浏览器", "LQ")]
    public void ChineseNames_MapToUppercaseInitials(string text, string expected)
    {
        Assert.Equal(expected, PinyinInitialService.ToInitials(text));
    }

    [Theory]
    [InlineData("Word2020", "WORD2020")]
    [InlineData("7-Zip", "7ZIP")]
    public void AsciiLettersAndDigits_ArePreservedAndUppercased(string text, string expected)
    {
        Assert.Equal(expected, PinyinInitialService.ToInitials(text));
    }

    [Fact]
    public void MixedChineseAndAscii_Concatenates()
    {
        Assert.Equal("QQLQ", PinyinInitialService.ToInitials("QQ浏览器"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInput_ReturnsEmpty(string? text)
    {
        Assert.Equal(string.Empty, PinyinInitialService.ToInitials(text));
    }

    [Fact]
    public void SymbolsAndWhitespace_AreSkipped()
    {
        Assert.Equal("AB", PinyinInitialService.ToInitials(" A#B "));
        Assert.Equal(string.Empty, PinyinInitialService.ToInitials("！@#￥%……&*（）"));
    }
}
