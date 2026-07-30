using LanFlow.Desktop.Services;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class IconCacheKeyTests
{
    [Fact]
    public void CacheKey_SeparatesSizeVersionAndTheme()
    {
        var baseline = IconCacheKey.Create(@"C:\Apps\Tool.exe", 48, 100, "dark");

        Assert.NotEqual(baseline, IconCacheKey.Create(@"C:\Apps\Tool.exe", 64, 100, "dark"));
        Assert.NotEqual(baseline, IconCacheKey.Create(@"C:\Apps\Tool.exe", 48, 101, "dark"));
        Assert.NotEqual(baseline, IconCacheKey.Create(@"C:\Apps\Tool.exe", 48, 100, "light"));
        Assert.Equal(baseline, IconCacheKey.Create(@"c:\apps\tool.exe", 48, 100, "dark"));
    }
}
