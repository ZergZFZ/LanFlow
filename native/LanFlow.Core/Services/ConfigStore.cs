using System.IO;
using System.Text.Json;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

public interface IConfigStore
{
    AppConfig Load();
    void Save(AppConfig config);
}

public sealed class ConfigStore : IConfigStore
{
    private readonly string _configDirectory;
    private readonly string _configPath;
    private readonly string _defaultHotkey;

    public ConfigStore(string defaultHotkey = "Ctrl+Alt+L", string? configDirectory = null)
    {
        _defaultHotkey = defaultHotkey;
        _configDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LanFlow");
        _configPath = Path.Combine(_configDirectory, "config.json");
    }

    public string ConfigPath => _configPath;

    public string ConfigDirectory => _configDirectory;

    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            return Normalize(new AppConfig(), isExistingConfig: false);
        }

        try
        {
            using var stream = File.OpenRead(_configPath);
            return Normalize(ConfigDocumentSerializer.Deserialize(stream), isExistingConfig: true);
        }
        catch (JsonException)
        {
            return Normalize(new AppConfig(), isExistingConfig: false);
        }
        catch (IOException)
        {
            return Normalize(new AppConfig(), isExistingConfig: false);
        }
    }

    private AppConfig Normalize(AppConfig config, bool isExistingConfig)
    {
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

        // 截图快捷键：空值回默认（旧配置无此字段时走这里）。
        if (string.IsNullOrWhiteSpace(settings.ScreenshotHotkey))
        {
            settings.ScreenshotHotkey = "Ctrl+Shift+A";
        }

        settings.Theme = settings.Theme == "light" ? "light" : "dark";
        settings.ThemeProfile = string.IsNullOrWhiteSpace(settings.ThemeProfile)
            ? (settings.Theme == "light" ? "浅色" : "深色")
            : settings.ThemeProfile;
        settings.ThemeColors ??= settings.Theme == "light" ? ThemeColors.Light() : ThemeColors.Dark();
        settings.CustomThemes ??= [];

            settings.LayoutMode = settings.LayoutMode switch
            {
                "tile" or "list" => SettingsOptionValues.GridLayout,
                SettingsOptionValues.GridLayout or SettingsOptionValues.CardLayout => settings.LayoutMode,
                _ => SettingsOptionValues.GridLayout,
            };
        settings.GroupLayout = settings.GroupLayout == SettingsOptionValues.GroupTop
            ? SettingsOptionValues.GroupTop
            : SettingsOptionValues.GroupLeft;
        settings.GroupSwitchMode = settings.GroupSwitchMode == SettingsOptionValues.GroupSwitchHover
            ? SettingsOptionValues.GroupSwitchHover
            : SettingsOptionValues.GroupSwitchClick;
        settings.AnimationMode = settings.AnimationMode is SettingsOptionValues.AnimationOn or SettingsOptionValues.AnimationOff
            ? settings.AnimationMode
            : SettingsOptionValues.AnimationSystem;

        if (string.IsNullOrWhiteSpace(settings.TransparencyMode))
        {
            settings.TransparencyMode = isExistingConfig
                ? SettingsOptionValues.TransparencyWholeWindow
                : SettingsOptionValues.TransparencyLayered;
            if (isExistingConfig) settings.WholeWindowOpacity = settings.Opacity;
        }
        else if (settings.TransparencyMode != SettingsOptionValues.TransparencyWholeWindow)
        {
            settings.TransparencyMode = SettingsOptionValues.TransparencyLayered;
        }

        settings.Opacity = Math.Clamp(settings.Opacity, 0.40, 1.00);
        SettingsNormalizer.ClampPreviewValues(settings);

        settings.Opacity = settings.TransparencyMode == SettingsOptionValues.TransparencyWholeWindow
            ? settings.WholeWindowOpacity
            : settings.LayeredOpacity;
        settings.IconSize = Math.Clamp(settings.IconSize, 24, 72);
        settings.CardWidth = Math.Clamp(settings.CardWidth, 48, 320);
        settings.CardHeight = Math.Clamp(settings.CardHeight, 48, 240);
        settings.CardSize = Math.Clamp(settings.CardSize, 76, 160);
        settings.TextSize = Math.Clamp(settings.TextSize, 10, 18);
        settings.ItemSpacing = Math.Clamp(settings.ItemSpacing, 0, 64);
        settings.RowSpacing = Math.Clamp(settings.RowSpacing, 0, 80);
        settings.ContentPadding = Math.Clamp(settings.ContentPadding, 6, 40);
        return config;
    }
    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Normalize(config, isExistingConfig: true);
        Directory.CreateDirectory(_configDirectory);
        var temporaryPath = _configPath + ".tmp";

        try
        {
            File.WriteAllBytes(temporaryPath, ConfigDocumentSerializer.Serialize(config));
            File.Move(temporaryPath, _configPath, true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch
            {
                // 清理失败不覆盖原始保存异常。
            }
            throw;
        }
    }
}
