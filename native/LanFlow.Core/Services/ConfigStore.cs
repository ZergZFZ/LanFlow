using System.IO;
using System.Text.Json;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

/// <summary>
/// 配置仓储抽象。对齐 Windows 主线 v2.0.1：ViewModel 依赖接口而非具体文件系统实现，
/// 使设置保存链路可以用记录型替身做单元测试，不必写真实磁盘。
/// </summary>
public interface IConfigStore
{
    /// <summary>平台默认全局热键（Linux 端由启动器注入，如 Ctrl+Alt+L）。</summary>
    string DefaultHotkey { get; }

    AppConfig Load();
    void Save(AppConfig config);
}

public sealed class ConfigStore : IConfigStore
{
    /// <summary>B5-4：当前配置版本。每次结构变更时递增，旧版本在 Load 时受控迁移。</summary>
    private const int CurrentConfigVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _configDirectory;
    private readonly string _configPath;

    public ConfigStore(string defaultHotkey = "Ctrl+Alt+Space")
    {
        DefaultHotkey = defaultHotkey;
        _configDirectory = ResolveConfigDirectory();
        _configPath = Path.Combine(_configDirectory, "config.json");
    }

    public string DefaultHotkey { get; }

    // B5-4/B3-6：配置目录统一解析——环境变量 LANFLOW_CONFIG_DIR 覆盖默认目录。
    // 供 SettingsWindow 性能页等外部复用，避免各自硬编码导致路径不一致。
    public static string ResolveConfigDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("LANFLOW_CONFIG_DIR");
        return string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LanFlow")
            : overrideDir;
    }

    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            return Normalize(new AppConfig());
        }

        try
        {
            using var stream = File.OpenRead(_configPath);
            return Normalize(JsonSerializer.Deserialize<AppConfig>(stream, SerializerOptions) ?? new AppConfig());
        }
        catch (JsonException)
        {
            return Normalize(new AppConfig());
        }
        catch (IOException)
        {
            return Normalize(new AppConfig());
        }
    }

    private AppConfig Normalize(AppConfig config)
    {
        // B5-4：受控迁移——旧配置（version 缺失=0）迁移到当前版本。
        // v0→v1：无需结构性改写（字段默认值兜底已在下方完成），仅补齐版本号。
        if (config.Version < CurrentConfigVersion)
        {
            config.Version = CurrentConfigVersion;
        }

        config.Groups ??= [];
        config.Settings ??= new Settings();
        // 字段级归一化统一收口到 SettingsPipeline（唯一口径）：
        // 加载与设置页保存共用同一套夹紧/枚举/旧热键迁移/派生规则。
        SettingsPipeline.Normalize(config.Settings, DefaultHotkey);
        return config;
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(_configDirectory);
        var temporaryPath = _configPath + ".tmp";
        var json = JsonSerializer.Serialize(config, SerializerOptions);

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _configPath, true);
    }
}
