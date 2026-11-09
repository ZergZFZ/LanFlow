using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Core.Tests;

/// <summary>记录型配置仓储：让设置保存链路的测试不必写真实磁盘。</summary>
internal sealed class RecordingStore : IConfigStore
{
    private readonly AppConfig _config;

    public RecordingStore(AppConfig config, string defaultHotkey = "Ctrl+Alt+L")
    {
        _config = config;
        DefaultHotkey = defaultHotkey;
    }

    public string DefaultHotkey { get; }
    public int SaveCallCount { get; private set; }
    public AppConfig? LastSavedConfig { get; private set; }

    public AppConfig Load() => _config;

    public void Save(AppConfig config)
    {
        SaveCallCount++;
        LastSavedConfig = config;
    }

    /// <summary>MainViewModel 构造时按 B5-3 约定会落盘一次，测试前清掉该基线。</summary>
    public void Reset()
    {
        SaveCallCount = 0;
        LastSavedConfig = null;
    }
}

/// <summary>
/// 临时配置目录。ConfigStore 通过 LANFLOW_CONFIG_DIR 解析路径，
/// 用例结束后恢复原值并清理目录；程序集已禁用并行，避免环境变量互相干扰。
/// </summary>
internal sealed class ConfigSandbox : IDisposable
{
    private readonly string? _previousOverride;

    public ConfigSandbox()
    {
        Root = Path.Combine(Path.GetTempPath(), "lanflow-tests-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Root);
        _previousOverride = Environment.GetEnvironmentVariable("LANFLOW_CONFIG_DIR");
        Environment.SetEnvironmentVariable("LANFLOW_CONFIG_DIR", Root);
    }

    public string Root { get; }
    public string ConfigPath => Path.Combine(Root, "config.json");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LANFLOW_CONFIG_DIR", _previousOverride);
        try
        {
            System.IO.Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言结果。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
