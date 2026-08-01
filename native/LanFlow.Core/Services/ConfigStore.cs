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
    private bool _saveBlocked;
    private string? _saveBlockedReason;

    // 读取失败时的用户可读警告；成功或文件不存在时为 null。
    // 调用方（如 MainWindow）在启动后检查并提示，避免“配置损坏被静默重置”造成数据丢失。
    public string? LastLoadWarning { get; private set; }

    public ConfigStore(string defaultHotkey = "Ctrl+Alt+Space", string? configDirectory = null)
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
        LastLoadWarning = null;
        _saveBlocked = false;
        _saveBlockedReason = null;
        if (!File.Exists(_configPath))
        {
            return Normalize(new AppConfig(), isExistingConfig: false);
        }

        try
        {
            using var stream = File.OpenRead(_configPath);
            var config = ConfigDocumentSerializer.Deserialize(stream);
            var migration = ConfigVersionMigrationService.Migrate(config);
            switch (migration.Status)
            {
                case ConfigVersionMigrationStatus.FutureVersion:
                    BlockSaves(migration.Message ?? "配置为未来版本，已阻断保存以保护原文件。");
                    return Normalize(new AppConfig(), isExistingConfig: false);
                case ConfigVersionMigrationStatus.Migrated:
                    if (!TryCreateMigrationBackup())
                    {
                        BlockSaves(
                            $"配置迁移前备份失败，已保留原文件并阻断保存：{_configPath}。请手动复制该文件后再继续。");
                        return Normalize(new AppConfig(), isExistingConfig: false);
                    }

                    return Normalize(config, isExistingConfig: true);
                default:
                    return Normalize(config, isExistingConfig: true);
            }
        }
        catch (JsonException)
        {
            return RecoverCorruptConfig("配置文件 JSON 格式损坏");
        }
        catch (IOException)
        {
            return RecoverCorruptConfig("配置文件读取失败（文件可能被其他程序占用）");
        }
    }

    // 配置读取失败时：先把原文件保留为 .corrupt-<时间戳> 备份，再启用新配置，
    // 避免后续任意保存把损坏的原始数据覆盖掉。备份失败时仍给出明确警告。
    private AppConfig RecoverCorruptConfig(string reason)
    {
        var backupPath = TryPreserveCorruptConfig();
        LastLoadWarning = backupPath is null
            ? $"{reason}，且无法自动备份原文件：{_configPath}。建议先手动复制该文件后再继续使用。"
            : $"{reason}，原文件已备份到：{backupPath}。LanFlow 已启用新配置，你的历史数据不会丢失。";
        return Normalize(new AppConfig(), isExistingConfig: false);
    }

    private string? TryPreserveCorruptConfig()
    {
        if (!File.Exists(_configPath))
        {
            return null;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = $"{_configPath}.corrupt-{timestamp}";
        var suffix = 2;
        while (File.Exists(backupPath))
        {
            backupPath = $"{_configPath}.corrupt-{timestamp}-{suffix++}";
        }

        try
        {
            File.Move(_configPath, backupPath);
            return backupPath;
        }
        catch
        {
            // 重命名失败（例如文件被占用）时退化为复制；复制也失败则交给调用方提示。
            try
            {
                File.Copy(_configPath, backupPath);
                return backupPath;
            }
            catch
            {
                return null;
            }
        }
    }

    private AppConfig Normalize(AppConfig config, bool isExistingConfig)
    {
        config.Settings ??= new Settings();
        var settings = config.Settings;
        // 新建或损坏恢复的配置补齐当前版本号；旧版本迁移已由 ConfigVersionMigrationService 完成。
        if (config.ConfigVersion < AppConfig.CurrentVersion)
        {
            config.ConfigVersion = AppConfig.CurrentVersion;
        }
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
        if (_saveBlocked)
        {
            throw new InvalidOperationException(
                _saveBlockedReason ?? "配置为未来版本，已阻断保存以保护原文件。请升级 LanFlow 后再保存。");
        }

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

    private void BlockSaves(string reason)
    {
        _saveBlocked = true;
        _saveBlockedReason = reason;
        LastLoadWarning = reason;
    }

    // 迁移前把原文件复制为时间戳备份，避免版本升级覆盖用户唯一副本。
    private bool TryCreateMigrationBackup()
    {
        if (!File.Exists(_configPath))
        {
            return true;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var backupPath = Path.Combine(
            _configDirectory,
            $"config.backup-{stamp}-{Guid.NewGuid():N}.json");
        try
        {
            File.Copy(_configPath, backupPath, overwrite: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            return false;
        }
    }
}
