using LanFlow.Desktop.Models;

namespace LanFlow.Core.Tests;

public sealed class SettingsPipelineTests
{
    [Fact]
    public void Writable_CoversAllSimpleSettingsAndGrowsWithNewFields()
    {
        var names = SettingsPipeline.Writable.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        // 这里只断言「集合由反射生成」，不写死字段总数：
        // 新增设置字段自动进入复制链路与完备性测试，无需登记白名单。
        Assert.Contains(nameof(Settings.Hotkey), names);
        Assert.Contains(nameof(Settings.ThemeColors), names);
        Assert.Contains(nameof(Settings.CustomThemes), names);
        Assert.Contains(nameof(Settings.GroupHoverDelayMs), names);
        Assert.Contains(nameof(Settings.WholeWindowOpacity), names);
        Assert.All(SettingsPipeline.Writable, p =>
        {
            Assert.True(p.CanRead && p.CanWrite, $"{p.Name} 不应出现在可写属性集合中");
            Assert.Empty(p.GetIndexParameters());
        });
    }

    [Fact]
    public void Normalize_ClampsEveryNumericSetting()
    {
        var settings = new Settings
        {
            Opacity = 99,
            LayeredOpacity = -1,
            WholeWindowOpacity = 99,
            IconSize = 1,
            CardWidth = 1,
            CardHeight = 9999,
            CardSize = 1,
            TextSize = 999,
            ItemSpacing = -10,
            RowSpacing = 999,
            ContentPadding = -5,
            GroupHoverDelayMs = 9999,
        };

        SettingsPipeline.Normalize(settings, "Ctrl+Alt+L");

        Assert.Equal(0.55, settings.LayeredOpacity);
        Assert.Equal(1.0, settings.WholeWindowOpacity);
        Assert.Equal(0.55, settings.Opacity);
        Assert.Equal(24, settings.IconSize);
        Assert.Equal(48, settings.CardWidth);
        Assert.Equal(240, settings.CardHeight);
        Assert.Equal(76, settings.CardSize);
        Assert.Equal(18, settings.TextSize);
        Assert.Equal(0, settings.ItemSpacing);
        Assert.Equal(80, settings.RowSpacing);
        Assert.Equal(6, settings.ContentPadding);
        Assert.Equal(1000, settings.GroupHoverDelayMs);
    }

    [Fact]
    public void Normalize_FillsNullThemesAndDerivesOpacityPerMode()
    {
        var settings = new Settings
        {
            Theme = "light",
            ThemeProfile = "  ",
            ThemeColors = null!,
            CustomThemes = null!,
            TransparencyMode = "wholeWindow",
            LayeredOpacity = 0.60,
            WholeWindowOpacity = 0.90,
        };

        SettingsPipeline.Normalize(settings, "Ctrl+Alt+L");

        Assert.Equal("浅色", settings.ThemeProfile);
        Assert.NotNull(settings.ThemeColors);
        Assert.NotNull(settings.CustomThemes);
        Assert.Equal("#F6F7FB", settings.ThemeColors.Panel);
        Assert.Equal(0.90, settings.Opacity);
    }

    [Fact]
    public void Clone_ToleratesNullReferenceFields()
    {
        var source = new Settings { ThemeColors = null!, CustomThemes = null! };

        var clone = source.Clone();

        Assert.NotNull(clone.ThemeColors);
        Assert.NotNull(clone.CustomThemes);
    }

    [Fact]
    public void Normalize_RejectsEmptyPlatformDefaultHotkey()
    {
        Assert.Throws<ArgumentException>(() => SettingsPipeline.Normalize(new Settings(), "  "));
    }
}
