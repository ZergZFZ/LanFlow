using System.IO;
using System.Text.Json;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

public enum ConfigMigrationStatus
{
    Success,
    NoChange,
    TargetContainsConfig,
    InvalidTarget,
    WriteFailed,
    ValidationFailed,
}

public sealed record ConfigMigrationResult(
    ConfigMigrationStatus Status,
    string TargetConfigPath,
    string? BackupPath,
    string? Error);

/// <summary>
/// 把当前配置写入目标目录并在验证成功后更新 locator；源配置保留，覆盖前生成时间戳备份。
/// </summary>
public sealed class ConfigMigrationService
{
    private readonly ConfigLocationService _locationService;
    private readonly Func<DateTimeOffset> _utcNow;

    public ConfigMigrationService(
        ConfigLocationService locationService,
        Func<DateTimeOffset>? utcNow = null)
    {
        _locationService = locationService ?? throw new ArgumentNullException(nameof(locationService));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public ConfigMigrationResult Migrate(
        AppConfig config,
        string currentDirectory,
        string targetDirectory,
        bool overwriteExisting)
    {
        ArgumentNullException.ThrowIfNull(config);

        string current;
        string target;
        try
        {
            current = ConfigLocationService.NormalizeDirectory(currentDirectory);
            target = ConfigLocationService.NormalizeDirectory(targetDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException)
        {
            return new ConfigMigrationResult(
                ConfigMigrationStatus.InvalidTarget,
                targetDirectory ?? string.Empty,
                BackupPath: null,
                ex.Message);
        }

        string targetConfig = Path.Combine(target, "config.json");
        if (ConfigLocationService.PathsEqual(current, target))
        {
            return new ConfigMigrationResult(
                ConfigMigrationStatus.NoChange,
                targetConfig,
                BackupPath: null,
                Error: null);
        }

        string tempPath = targetConfig + ".tmp";
        string? backupPath = null;
        bool targetConfigReplaced = false;
        try
        {
            try
            {
                Directory.CreateDirectory(target);
                if (File.Exists(targetConfig) && !overwriteExisting)
                {
                    return new ConfigMigrationResult(
                        ConfigMigrationStatus.TargetContainsConfig,
                        targetConfig,
                        BackupPath: null,
                        Error: null);
                }

                File.WriteAllBytes(tempPath, ConfigDocumentSerializer.Serialize(config));
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or JsonException)
            {
                return new ConfigMigrationResult(
                    ConfigMigrationStatus.WriteFailed,
                    targetConfig,
                    BackupPath: null,
                    ex.Message);
            }

            try
            {
                using var validationStream = File.OpenRead(tempPath);
                _ = ConfigDocumentSerializer.Deserialize(validationStream);
            }
            catch (Exception ex) when (ex is JsonException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException)
            {
                return new ConfigMigrationResult(
                    ConfigMigrationStatus.ValidationFailed,
                    targetConfig,
                    BackupPath: null,
                    ex.Message);
            }

            try
            {
                if (File.Exists(targetConfig))
                {
                    string stamp = _utcNow().ToString("yyyyMMdd-HHmmssfff");
                    backupPath = Path.Combine(
                        target,
                        $"config.backup-{stamp}-{Guid.NewGuid():N}.json");
                    File.Copy(targetConfig, backupPath, overwrite: false);
                }

                File.Move(tempPath, targetConfig, overwrite: true);
                targetConfigReplaced = true;
                if (ConfigLocationService.PathsEqual(target, _locationService.DefaultDirectory))
                {
                    _locationService.UseDefaultDirectory();
                }
                else
                {
                    _locationService.SetCustomDirectory(target);
                }
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
                if (targetConfigReplaced)
                {
                    RestoreTargetConfig(targetConfig, backupPath);
                }
                return new ConfigMigrationResult(
                    ConfigMigrationStatus.WriteFailed,
                    targetConfig,
                    backupPath,
                    ex.Message);
            }

            return new ConfigMigrationResult(
                ConfigMigrationStatus.Success,
                targetConfig,
                backupPath,
                Error: null);
        }
        finally
        {
            DeleteTempFile(tempPath);
        }
    }

    public ConfigMigrationResult RestoreDefault(
        AppConfig config,
        string currentDirectory,
        bool overwriteExisting) =>
        Migrate(
            config,
            currentDirectory,
            _locationService.DefaultDirectory,
            overwriteExisting);

    private static void RestoreTargetConfig(string targetConfig, string? backupPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
            {
                File.Copy(backupPath, targetConfig, overwrite: true);
            }
            else if (File.Exists(targetConfig))
            {
                File.Delete(targetConfig);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
    private static void DeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
