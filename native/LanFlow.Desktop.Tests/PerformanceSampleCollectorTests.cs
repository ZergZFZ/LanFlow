using System.Globalization;
using LanFlow.Desktop.Diagnostics;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class PerformanceSampleCollectorTests
{
    [Fact]
    public void Summarize_ReturnsNearestRankPercentiles()
    {
        var samples = Enumerable.Range(1, 100).Select(value => (double)value).ToArray();

        var summary = PerformanceSampleCollector.Summarize(samples);

        Assert.Equal(50, summary.P50);
        Assert.Equal(95, summary.P95);
        Assert.Equal(99, summary.P99);
        Assert.Equal(100, summary.Maximum);
    }

    [Fact]
    public void Summarize_RejectsEmptySamples()
    {
        Assert.Throws<ArgumentException>(() => PerformanceSampleCollector.Summarize([]));
    }

    [Fact]
    public void ExportCsv_IncludesEnvironmentAndCacheState()
    {
        var csv = PerformanceSampleCollector.ExportCsv(
            [new PerformanceSample("selection-ack", 42.5, "warm", "layered", 28)],
            new PerformanceEnvironment("Windows 11", "CPU", "GPU", "2560x1440", "125%"));

        Assert.Contains("cacheState,transparencyMode,realizedContainers", csv);
        Assert.Contains("warm,layered,28", csv);
        Assert.Contains("Windows 11,CPU,GPU,2560x1440,125%", csv);
    }

    [Fact]
    public void ExportCsv_UsesInvariantNumbersAndRfc4180Escaping()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var csv = PerformanceSampleCollector.ExportCsv(
                [new PerformanceSample("content,stable", 12.75, "cold", "whole\"window", 31)],
                new PerformanceEnvironment("Windows 11", "CPU, Model", "GPU\r\nModel", "1920x1080", "100%"));

            Assert.Contains("12.75", csv);
            Assert.Contains("\"content,stable\"", csv);
            Assert.Contains("\"whole\"\"window\"", csv);
            Assert.Contains("\"CPU, Model\"", csv);
            Assert.Contains("\"GPU\r\nModel\"", csv);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
