using System.IO;
using System.Text;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;
using Xunit;

namespace LanFlow.Core.Tests;

public class ConfigMigrationServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ConfigLocationService _location;
    private readonly ConfigMigrationService _migration;

    public ConfigMigrationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "lanflow-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _location = new ConfigLocationService(_root);
        _migration = new ConfigMigrationService(_location);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static AppConfig CreateConfig(int hoverDelay = 150)
    {
        var config = new AppConfig();
        config.Settings.GroupHoverDelayMs = hoverDelay;
        return config;
    }

    private string CreateDirectory(string name)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Migrate_ToEmptyDirectory_WritesConfigAndUpdatesLocator()
    {
        string target = CreateDirectory("target");

        var result = _migration.Migrate(CreateConfig(240), _location.DefaultDirectory, target, overwriteExisting: false);

        Assert.Equal(ConfigMigrationStatus.Success, result.Status);
        Assert.Null(result.BackupPath);
        Assert.True(File.Exists(Path.Combine(target, "config.json")));
        Assert.False(File.Exists(Path.Combine(target, "config.json.tmp")));

        var resolution = _location.Resolve();
        Assert.False(resolution.IsDefault);
        Assert.Equal(target, resolution.DirectoryPath);
    }

    [Fact]
    public void Migrate_ToSameDirectory_ReturnsNoChange()
    {
        string target = CreateDirectory("same");

        var result = _migration.Migrate(CreateConfig(), target, target, overwriteExisting: false);

        Assert.Equal(ConfigMigrationStatus.NoChange, result.Status);
    }

    [Fact]
    public void Migrate_TargetHasConfigWithoutOverwrite_ReturnsTargetContainsConfig()
    {
        string target = CreateDirectory("occupied");
        File.WriteAllText(Path.Combine(target, "config.json"), "{}", Encoding.UTF8);

        var result = _migration.Migrate(CreateConfig(), _location.DefaultDirectory, target, overwriteExisting: false);

        Assert.Equal(ConfigMigrationStatus.TargetContainsConfig, result.Status);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "config.json")));
        Assert.True(_location.Resolve().IsDefault);
    }

    [Fact]
    public void Migrate_TargetHasConfigWithOverwrite_CreatesBackup()
    {
        string target = CreateDirectory("overwrite");
        string targetConfig = Path.Combine(target, "config.json");
        File.WriteAllText(targetConfig, "{\"settings\":{}}", Encoding.UTF8);

        var result = _migration.Migrate(CreateConfig(320), _location.DefaultDirectory, target, overwriteExisting: true);

        Assert.Equal(ConfigMigrationStatus.Success, result.Status);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));
        Assert.Equal("{\"settings\":{}}", File.ReadAllText(result.BackupPath!));

        using var stream = File.OpenRead(targetConfig);
        Assert.Equal(320, ConfigDocumentSerializer.Deserialize(stream).Settings.GroupHoverDelayMs);
    }

    [Fact]
    public void Migrate_WithInvalidTarget_ReturnsInvalidTarget()
    {
        var result = _migration.Migrate(CreateConfig(), _location.DefaultDirectory, "  |invalid?*", overwriteExisting: false);

        Assert.True(
            result.Status is ConfigMigrationStatus.InvalidTarget or ConfigMigrationStatus.WriteFailed,
            $"unexpected status {result.Status}");
        Assert.True(_location.Resolve().IsDefault);
    }

    [Fact]
    public void Migrate_KeepsSourceConfigFile()
    {
        string source = CreateDirectory("source");
        string sourceConfig = Path.Combine(source, "config.json");
        File.WriteAllBytes(sourceConfig, ConfigDocumentSerializer.Serialize(CreateConfig(100)));
        string target = CreateDirectory("dest");

        var result = _migration.Migrate(CreateConfig(100), source, target, overwriteExisting: false);

        Assert.Equal(ConfigMigrationStatus.Success, result.Status);
        Assert.True(File.Exists(sourceConfig));
    }

    [Fact]
    public void RestoreDefault_MovesConfigBackAndClearsLocator()
    {
        string custom = CreateDirectory("custom");
        Assert.Equal(
            ConfigMigrationStatus.Success,
            _migration.Migrate(CreateConfig(200), _location.DefaultDirectory, custom, overwriteExisting: false).Status);

        var result = _migration.RestoreDefault(CreateConfig(200), custom, overwriteExisting: true);

        Assert.Equal(ConfigMigrationStatus.Success, result.Status);
        Assert.False(File.Exists(_location.LocatorPath));
        Assert.True(File.Exists(Path.Combine(_location.DefaultDirectory, "config.json")));
        Assert.True(_location.Resolve().IsDefault);
    }

    [Fact]
    public void Migrate_WhenLocatorUpdateFails_RestoresExistingTargetConfig()
    {
        string target = CreateDirectory("locator-failure");
        string targetConfig = Path.Combine(target, "config.json");
        const string original = "{\"settings\":{\"groupHoverDelayMs\":210}}";
        File.WriteAllText(targetConfig, original, Encoding.UTF8);

        Directory.CreateDirectory(_location.DefaultDirectory);
        Directory.CreateDirectory(_location.LocatorPath);

        var result = _migration.Migrate(CreateConfig(320), _location.DefaultDirectory, target, overwriteExisting: true);

        Assert.Equal(ConfigMigrationStatus.WriteFailed, result.Status);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(original, File.ReadAllText(targetConfig));
        Assert.True(_location.Resolve().IsDefault);
    }


    [Fact]
    public void Migrate_WrittenConfigIsReadableByConfigStore()
    {
        string target = CreateDirectory("readable");

        _migration.Migrate(CreateConfig(410), _location.DefaultDirectory, target, overwriteExisting: false);

        var store = new ConfigStore(configDirectory: target);
        // 410 超过上限 500? 未超出，保持原值。
        Assert.Equal(410, store.Load().Settings.GroupHoverDelayMs);
    }
}
