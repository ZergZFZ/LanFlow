using System.Diagnostics;
using System.IO;
using System.Windows;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

/// <summary>
/// 配置位置维护：显示当前位置、打开所在目录、更换到自定义目录、恢复默认目录。
/// 供设置窗口“配置位置”面板使用。
/// </summary>
public sealed class SettingsMaintenanceService
{
    private readonly IConfigStore _configStore;
    private readonly ConfigLocationService _locationService;
    private readonly ConfigMigrationService _migrationService;

    public SettingsMaintenanceService(
        IConfigStore configStore,
        ConfigMigrationService migrationService,
        ConfigLocationService locationService)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
        _locationService = locationService ?? throw new ArgumentNullException(nameof(locationService));
    }

    public ConfigLocationResolution Resolve() => _locationService.Resolve();

    public string DefaultDirectory => _locationService.DefaultDirectory;

    public string ConfigDirectory => Resolve().DirectoryPath;

    public string ConfigPath => Resolve().ConfigPath;

    public bool IsDefaultLocation => Resolve().IsDefault;

    /// <summary>更换到自定义目录；成功后需重启才会生效。</summary>
    public ConfigMigrationResult ChangeLocation(string targetDirectory, bool overwriteExisting)
    {
        var current = Resolve();
        return _migrationService.Migrate(
            LoadConfig(),
            current.DirectoryPath,
            targetDirectory,
            overwriteExisting);
    }

    /// <summary>恢复到默认目录 %APPDATA%\LanFlow。</summary>
    public ConfigMigrationResult RestoreDefaultLocation(bool overwriteExisting)
    {
        var current = Resolve();
        return _migrationService.RestoreDefault(LoadConfig(), current.DirectoryPath, overwriteExisting);
    }

    public void CopyConfigPathToClipboard() => Clipboard.SetText(ConfigPath);

    public void OpenConfigLocation()
    {
        string directory = ConfigDirectory;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// 创建完整配置备份：时间戳命名的只读快照文件，不修改当前配置。
    /// 返回备份文件路径；失败抛异常由调用方提示。
    /// </summary>
    public string CreateBackup()
    {
        var resolution = Resolve();
        string sourcePath = resolution.ConfigPath;
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("尚未生成配置文件，无需备份。", sourcePath);
        }

        string directory = resolution.DirectoryPath;
        Directory.CreateDirectory(directory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(
            directory,
            $"LanFlow-backup-v1-{stamp}.json");
        var suffix = 2;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(
                directory,
                $"LanFlow-backup-v1-{stamp}-{suffix++}.json");
        }

        File.Copy(sourcePath, backupPath, overwrite: false);
        File.SetAttributes(backupPath, FileAttributes.ReadOnly);
        return backupPath;
    }

    private AppConfig LoadConfig() => _configStore.Load();
}

/// <summary>配置位置操作的用户提示文案，集中管理便于测试与复用。</summary>
public static class SettingsMaintenanceMessages
{
    public static string Describe(ConfigMigrationResult result) => result.Status switch
    {
        ConfigMigrationStatus.Success => result.BackupPath is null
            ? "配置位置已更新，重启 LanFlow 后生效。"
            : "配置位置已更新（原文件已备份），重启 LanFlow 后生效。",
        ConfigMigrationStatus.NoChange => "目标目录与当前位置相同，未做更改。",
        ConfigMigrationStatus.TargetContainsConfig => "目标目录已存在 config.json，请确认是否覆盖。",
        ConfigMigrationStatus.InvalidTarget => "目标目录无效，请重新选择。",
        ConfigMigrationStatus.WriteFailed => "写入目标目录失败：" + (result.Error ?? "未知错误"),
        ConfigMigrationStatus.ValidationFailed => "写入后校验失败，已保留原配置。",
        _ => "操作未完成。",
    };

    public static string DescribeWarning(string? warning) => warning switch
    {
        null => string.Empty,
        "locator-empty" => "配置位置记录为空，已回退到默认目录。",
        "locator-invalid" => "配置位置记录损坏，已回退到默认目录。",
        "locator-directory-missing" => "自定义配置目录不存在，已回退到默认目录。",
        _ => "配置位置记录异常，已回退到默认目录。",
    };
}
