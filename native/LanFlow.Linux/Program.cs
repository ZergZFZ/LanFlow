using System;
using Avalonia;
using Avalonia.Logging;
using LanFlow.Desktop;

namespace LanFlow.Linux;

/// <summary>
/// 第三轮取证件（缺陷板 v2 §3）：Warning 及以上全量输出到 stdout；
/// Verbose 级仅限 Layout / Control / Binding / Visual 四个区域。
/// lanflow.sh 会把 stdout tee 成日志文件带回。
/// </summary>
internal sealed class ForensicLogSink : ILogSink
{
    private static readonly string[] VerboseAreas = { "Layout", "Control", "Binding", "Visual" };

    public bool IsEnabled(LogEventLevel level, string area)
    {
        if (level >= LogEventLevel.Warning)
        {
            return true;
        }

        foreach (var verboseArea in VerboseAreas)
        {
            if (string.Equals(verboseArea, area, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void Log(LogEventLevel level, string area, object source, string messageTemplate)
        => Console.WriteLine($"[Avalonia:{area}:{level}] {messageTemplate}");

    public void Log(LogEventLevel level, string area, object source, string messageTemplate, object[] propertyValues)
    {
        var extra = propertyValues is { Length: > 0 } ? " | " + string.Join(", ", propertyValues) : string.Empty;
        Console.WriteLine($"[Avalonia:{area}:{level}] {messageTemplate}{extra}");
    }
}

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 第三轮取证件（缺陷板 v2 §3.4）：全局异常捕获，防止静默失败无日志。
        // 警告：不要在这里碰 Dispatcher.UIThread——Avalonia 初始化前访问它会以
        // NullDispatcherImpl 固化调度器，MainLoop 直接抛 PlatformNotSupportedException（D9）。
        // UI 线程钩子改在 App.OnFrameworkInitializationCompleted 里订阅。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.WriteLine("[取证] AppDomain未处理异常: " + e.ExceptionObject);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.WriteLine("[取证] 未观察Task异常: " + e.Exception);
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        Logger.Sink = new ForensicLogSink();
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
