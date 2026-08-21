using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Core.Tests;

public sealed class ConfigVersionMigrationServiceTests
{
    [Fact]
    public void VersionZero_MigratesToCurrentVersion()
    {
        var config = new AppConfig { ConfigVersion = 0 };
        config.Groups.Add(new Group { Id = "g1", Name = "办公" });

        var result = ConfigVersionMigrationService.Migrate(config);

        Assert.Equal(ConfigVersionMigrationStatus.Migrated, result.Status);
        Assert.Equal(0, result.FromVersion);
        Assert.Equal(AppConfig.CurrentVersion, result.ToVersion);
        Assert.Equal(AppConfig.CurrentVersion, result.Config.ConfigVersion);
    }

    [Fact]
    public void Migration_PreservesUserDataFields()
    {
        var config = new AppConfig { ConfigVersion = 0 };
        var group = new Group { Id = "g1", Name = "办公" };
        group.Items.Add(new LauncherItem { Id = "i1", Name = "记事本", Path = @"C:\Windows\notepad.exe", UseCount = 3 });
        config.Groups.Add(group);
        config.Settings.Hotkey = "Ctrl+Alt+L";
        config.Settings.LayoutMode = "card";

        var result = ConfigVersionMigrationService.Migrate(config);

        var migrated = result.Config;
        Assert.Equal("g1", Assert.Single(migrated.Groups).Id);
        Assert.Equal("办公", migrated.Groups[0].Name);
        Assert.Equal("i1", Assert.Single(migrated.Groups[0].Items).Id);
        Assert.Equal(3, migrated.Groups[0].Items[0].UseCount);
        Assert.Equal("Ctrl+Alt+L", migrated.Settings.Hotkey);
        Assert.Equal("card", migrated.Settings.LayoutMode);
    }

    [Fact]
    public void CurrentVersion_ReturnsUpToDateWithoutMessage()
    {
        var config = new AppConfig { ConfigVersion = AppConfig.CurrentVersion };

        var result = ConfigVersionMigrationService.Migrate(config);

        Assert.Equal(ConfigVersionMigrationStatus.UpToDate, result.Status);
        Assert.Same(config, result.Config);
        Assert.Null(result.Message);
    }

    [Fact]
    public void FutureVersion_IsBlockedAndLeftUntouched()
    {
        var future = AppConfig.CurrentVersion + 5;
        var config = new AppConfig { ConfigVersion = future };

        var result = ConfigVersionMigrationService.Migrate(config);

        Assert.Equal(ConfigVersionMigrationStatus.FutureVersion, result.Status);
        Assert.Same(config, result.Config);
        Assert.Equal(future, result.Config.ConfigVersion);
        Assert.NotNull(result.Message);
        Assert.Contains("升级", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigVersionMigrationService.Migrate(null!));
    }
}
