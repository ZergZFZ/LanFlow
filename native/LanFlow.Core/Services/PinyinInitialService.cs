using System.Text;

namespace LanFlow.Desktop.Services;

/// <summary>
/// 汉字 → 拼音首字母转换（方案 B）。
/// 基于 GB2312 一级字库（3755 个常用字按拼音顺序排列）的区间表，
/// 无需引入完整拼音词库，覆盖常见软件名称用字。
/// </summary>
public static class PinyinInitialService
{
    // GB2312 一级字库区间表：起始码（含）→ 结束码（含）→ 拼音首字母。
    // 区间顺序与字库拼音排序一致，因此只需 23 个区间即可覆盖一级字库。
    private static readonly (int Low, int High, char Letter)[] Gb2312Ranges =
    [
        (0xB0A1, 0xB0C4, 'A'),
        (0xB0C5, 0xB2C0, 'B'),
        (0xB2C1, 0xB4ED, 'C'),
        (0xB4EE, 0xB6E9, 'D'),
        (0xB6EA, 0xB7A1, 'E'),
        (0xB7A2, 0xB8C0, 'F'),
        (0xB8C1, 0xB9FD, 'G'),
        (0xB9FE, 0xBBF6, 'H'),
        (0xBBF7, 0xBFA5, 'J'),
        (0xBFA6, 0xC0AB, 'K'),
        (0xC0AC, 0xC2E7, 'L'),
        (0xC2E8, 0xC4C2, 'M'),
        (0xC4C3, 0xC5B5, 'N'),
        (0xC5B6, 0xC5BD, 'O'),
        (0xC5BE, 0xC6D9, 'P'),
        (0xC6DA, 0xC8BA, 'Q'),
        (0xC8BB, 0xC8F5, 'R'),
        (0xC8F6, 0xCBF9, 'S'),
        (0xCBFA, 0xCDD9, 'T'),
        (0xCDDA, 0xCEF3, 'W'),
        (0xCEF4, 0xD1B8, 'X'),
        (0xD1B9, 0xD4D0, 'Y'),
        (0xD4D1, 0xD7F9, 'Z'),
    ];

    private static readonly Encoding Gb2312 = CreateGb2312Encoding();

    private static Encoding CreateGb2312Encoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("GB2312");
    }

    /// <summary>
    /// 返回文本的拼音首字母串（大写，仅字母与数字）。
    /// 非汉字、非 ASCII 字符被忽略；空格会被跳过。
    /// 示例：ToInitials("微信") == "WX"；ToInitials("幻兽帕鲁") == "HSPL"。
    /// </summary>
    public static string ToInitials(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(char.ToUpperInvariant(ch));
                continue;
            }

            if (ch < 0x4E00 || ch > 0x9FFF)
            {
                continue;
            }

            var initial = ToInitial(ch);
            if (initial is not null)
            {
                builder.Append(initial);
            }
        }

        return builder.ToString();
    }

    private static char? ToInitial(char ch)
    {
        var bytes = Gb2312.GetBytes(ch.ToString());
        if (bytes.Length != 2)
        {
            return null;
        }

        var code = (bytes[0] << 8) | bytes[1];
        if (code < 0xB0A1 || code > 0xD7F9)
        {
            // 二级字库或未收录字符：不参与首字母映射。
            return null;
        }

        foreach (var (low, high, letter) in Gb2312Ranges)
        {
            if (code >= low && code <= high)
            {
                return letter;
            }
        }

        return null;
    }
}
