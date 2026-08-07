using System.IO;
using System.Text.Json;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

public sealed class ConfigStore
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
    private readonly string _defaultHotkey;

    public ConfigStore(string defaultHotkey = "Ctrl+Alt+Space")
    {
        _defaultHotkey = defaultHotkey;
        // B5-4：支持换位置——环境变量 LANFLOW_CONFIG_DIR 覆盖配置目录（UOS 无 UI 迁移需求的最小支持）。
        var overrideDir = Environment.GetEnvironmentVariable("LANFLOW_CONFIG_DIR");
        _configDirectory = string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LanFlow")
            : overrideDir;
        _configPath = Path.Combine(_configDirectory, "config.json");
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
        // v0→v1：无需结构性改写（字段默认值兜底已在下方 Normalize 完成），仅补齐版本号。
        if (config.Version < CurrentConfigVersion)
        {
            config.Version = CurrentConfigVersion;
        }

        config.Settings ??= new Settings();
        var settings = config.Settings;
        // 空热键用平台默认值；若仍是旧默认 Alt+Space（常被窗口管理器占用），
        // 一键迁移到平台默认，避免全局热键静默失效。
        if (string.IsNullOrWhiteSpace(settings.Hotkey))
        {
            settings.Hotkey = _defaultHotkey;
        }
        else if (settings.Hotkey.Equals("Alt+Space", StringComparison.OrdinalIgnoreCase))
        {
            settings.Hotkey = _defaultHotkey;
        }
        settings.Theme = settings.Theme == "light" ? "light" : "dark";
        settings.ThemeProfile = string.IsNullOrWhiteSpace(settings.ThemeProfile)
            ? (settings.Theme == "light" ? "浅色" : "深色")
            : settings.ThemeProfile;
        settings.ThemeColors ??= settings.Theme == "light" ? ThemeColors.Light() : ThemeColors.Dark();
        settings.CustomThemes ??= [];
        settings.LayoutMode = settings.LayoutMode == "card" ? "card" : "tile";
        settings.Opacity = Math.Clamp(settings.Opacity, 0.55, 1.0);
        settings.IconSize = Math.Clamp(settings.IconSize, 24, 72);
        settings.CardWidth = Math.Clamp(settings.CardWidth, 48, 320);
        settings.CardHeight = Math.Clamp(settings.CardHeight, 48, 240);
        settings.CardSize = Math.Clamp(settings.CardSize, 76, 160);
        settings.TextSize = Math.Clamp(settings.TextSize, 10, 18);
        settings.ItemSpacing = Math.Clamp(settings.ItemSpacing, 0, 64);
        settings.RowSpacing = Math.Clamp(settings.RowSpacing, 0, 80);
        settings.ContentPadding = Math.Clamp(settings.ContentPadding, 6, 40);
        settings.GroupLayout = settings.GroupLayout == "top" ? "top" : "left";
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
