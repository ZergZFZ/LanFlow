namespace LanFlow.Desktop.Diagnostics;

public sealed record PerformanceSample(
    string Marker,
    double ElapsedMs,
    string CacheState,
    string TransparencyMode,
    int RealizedContainers);

public sealed record PerformanceEnvironment(
    string Os,
    string Cpu,
    string Gpu,
    string Resolution,
    string Scale);

public sealed record PerformanceSummary(
    double P50,
    double P95,
    double P99,
    double Maximum);
