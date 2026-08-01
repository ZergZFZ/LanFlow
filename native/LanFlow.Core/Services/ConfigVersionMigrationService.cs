using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

public enum ConfigVersionMigrationStatus
{
    UpToDate,
    Migrated,
    FutureVersion,
}

public sealed record ConfigVersionMigrationResult(
    ConfigVersionMigrationStatus Status,
    AppConfig Config,
    int FromVersion,
    int ToVersion,
    string? Message);

/// <summary>
/// 配置结构版本迁移：0 → 1 只补版本号，不重排、不删改用户字段；
/// 声明版本高于客户端支持的最高版本时返回 FutureVersion，由调用方保留原文件并阻断保存。
/// 迁移函数为纯函数：输入旧配置副本，输出新配置副本，不写文件。
/// </summary>
public static class ConfigVersionMigrationService
{
    public static ConfigVersionMigrationResult Migrate(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var from = config.ConfigVersion;
        if (from == AppConfig.CurrentVersion)
        {
            return new ConfigVersionMigrationResult(
                ConfigVersionMigrationStatus.UpToDate,
                config,
                from,
                AppConfig.CurrentVersion,
                Message: null);
        }

        if (from > AppConfig.CurrentVersion)
        {
            return new ConfigVersionMigrationResult(
                ConfigVersionMigrationStatus.FutureVersion,
                config,
                from,
                AppConfig.CurrentVersion,
                $"配置文件版本 {from} 高于当前客户端支持的版本 {AppConfig.CurrentVersion}，已保留原文件并阻断保存。请升级 LanFlow 后再使用。");
        }

        // 当前只有 0 -> 1 一条迁移路径：补版本号，其余字段保持不变。
        config.ConfigVersion = AppConfig.CurrentVersion;
        return new ConfigVersionMigrationResult(
            ConfigVersionMigrationStatus.Migrated,
            config,
            from,
            AppConfig.CurrentVersion,
            $"配置已从版本 {from} 迁移到 {AppConfig.CurrentVersion}。");
    }
}
