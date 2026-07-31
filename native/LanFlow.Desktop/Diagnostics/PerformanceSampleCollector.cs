using System.Globalization;
using System.Text;

namespace LanFlow.Desktop.Diagnostics;

public static class PerformanceSampleCollector
{
    public static PerformanceSummary Summarize(IEnumerable<double> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        double[] ordered = samples.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("At least one performance sample is required.", nameof(samples));
        }

        if (ordered.Any(value => !double.IsFinite(value)))
        {
            throw new ArgumentException("Performance samples must be finite numbers.", nameof(samples));
        }

        return new PerformanceSummary(
            NearestRank(ordered, 0.50),
            NearestRank(ordered, 0.95),
            NearestRank(ordered, 0.99),
            ordered[^1]);
    }

    public static string ExportCsv(
        IEnumerable<PerformanceSample> samples,
        PerformanceEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(environment);

        var csv = new StringBuilder();
        csv.AppendLine("os,cpu,gpu,resolution,scale,marker,elapsedMs,cacheState,transparencyMode,realizedContainers");
        foreach (PerformanceSample sample in samples)
        {
            ArgumentNullException.ThrowIfNull(sample);
            AppendField(csv, environment.Os);
            AppendField(csv, environment.Cpu);
            AppendField(csv, environment.Gpu);
            AppendField(csv, environment.Resolution);
            AppendField(csv, environment.Scale);
            AppendField(csv, sample.Marker);
            AppendField(csv, sample.ElapsedMs.ToString("R", CultureInfo.InvariantCulture));
            AppendField(csv, sample.CacheState);
            AppendField(csv, sample.TransparencyMode);
            AppendField(csv, sample.RealizedContainers.ToString(CultureInfo.InvariantCulture), isLast: true);
            csv.Append("\r\n");
        }

        return csv.ToString();
    }

    private static double NearestRank(IReadOnlyList<double> ordered, double percentile)
    {
        int index = Math.Max(0, (int)Math.Ceiling(percentile * ordered.Count) - 1);
        return ordered[index];
    }

    private static void AppendField(StringBuilder csv, string? value, bool isLast = false)
    {
        value ??= string.Empty;
        bool needsQuotes = value.IndexOfAny([',', '"', '\r', '\n']) >= 0;
        if (needsQuotes)
        {
            csv.Append('"');
            csv.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            csv.Append('"');
        }
        else
        {
            csv.Append(value);
        }

        if (!isLast)
        {
            csv.Append(',');
        }
    }
}
