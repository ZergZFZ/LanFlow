using System.IO;
using System.Text;
using FlickD.Desktop.Services;
using Xunit;

namespace FlickD.Core.Tests;

public class ConfigLocationServiceTests : IDisposable
{
    private readonly string _root;

    public ConfigLocationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "flickd-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private ConfigLocationService CreateService() => new(_root);

    [Fact]
    public void Resolve_WithoutLocator_ReturnsDefaultDirectory()
    {
        var service = CreateService();

        var resolution = service.Resolve();

        Assert.True(resolution.IsDefault);
        Assert.Null(resolution.Warning);
        Assert.Equal(Path.Combine(_root, "FlickD"), resolution.DirectoryPath);
        Assert.Equal(Path.Combine(_root, "FlickD", "config.json"), resolution.ConfigPath);
    }

    [Fact]
    public void Resolve_LegacyDefaultDirectory_CopiesConfigToNewDirectory()
    {
        string legacy = Path.Combine(_root, "LanFlow");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "config.json"), "{\"items\":[]}", Encoding.UTF8);

        var resolution = CreateService().Resolve();

        Assert.True(resolution.IsDefault);
        Assert.True(File.Exists(Path.Combine(_root, "FlickD", "config.json")));
        // 旧目录保留，便于回退。
        Assert.True(File.Exists(Path.Combine(legacy, "config.json")));
    }

    [Fact]
    public void Resolve_LegacyLocator_CopiesLocatorSoCustomDirectorySurvivesRename()
    {
        string legacy = Path.Combine(_root, "LanFlow");
        string custom = Path.Combine(_root, "custom");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(custom);
        File.WriteAllText(
            Path.Combine(legacy, "config-location.json"),
            "{\"configDirectory\":\"" + custom.Replace("\\", "\\\\") + "\"}",
            Encoding.UTF8);

        var resolution = CreateService().Resolve();

        Assert.False(resolution.IsDefault);
        Assert.Equal(custom, resolution.DirectoryPath);
    }

    [Fact]
    public void Resolve_NewDirectoryAlreadyHasConfig_DoesNotOverwriteFromLegacy()
    {
        string legacy = Path.Combine(_root, "LanFlow");
        string current = Path.Combine(_root, "FlickD");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "config.json"), "legacy", Encoding.UTF8);
        File.WriteAllText(Path.Combine(current, "config.json"), "current", Encoding.UTF8);

        CreateService().Resolve();

        Assert.Equal("current", File.ReadAllText(Path.Combine(current, "config.json")));
    }

    [Fact]
    public void SetCustomDirectory_ThenResolve_ReturnsCustomDirectory()
    {
        var service = CreateService();
        string custom = Path.Combine(_root, "custom");
        Directory.CreateDirectory(custom);

        service.SetCustomDirectory(custom);
        var resolution = service.Resolve();

        Assert.False(resolution.IsDefault);
        Assert.Equal(custom, resolution.DirectoryPath);
        Assert.Equal(Path.Combine(custom, "config.json"), resolution.ConfigPath);
        Assert.True(File.Exists(service.LocatorPath));
    }

    [Fact]
    public void SetCustomDirectory_WithDefaultPath_RemovesLocator()
    {
        var service = CreateService();
        string custom = Path.Combine(_root, "custom");
        Directory.CreateDirectory(custom);
        service.SetCustomDirectory(custom);

        Directory.CreateDirectory(service.DefaultDirectory);
        service.SetCustomDirectory(service.DefaultDirectory);

        Assert.False(File.Exists(service.LocatorPath));
        Assert.True(service.Resolve().IsDefault);
    }

    [Fact]
    public void SetCustomDirectory_MissingDirectory_Throws()
    {
        var service = CreateService();

        Assert.Throws<DirectoryNotFoundException>(
            () => service.SetCustomDirectory(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void Resolve_WithCorruptedLocator_FallsBackWithWarning()
    {
        var service = CreateService();
        Directory.CreateDirectory(service.DefaultDirectory);
        File.WriteAllText(service.LocatorPath, "{ not json", Encoding.UTF8);

        var resolution = service.Resolve();

        Assert.True(resolution.IsDefault);
        Assert.Equal("locator-invalid", resolution.Warning);
    }

    [Fact]
    public void Resolve_WithMissingTargetDirectory_FallsBackWithWarning()
    {
        var service = CreateService();
        Directory.CreateDirectory(service.DefaultDirectory);
        string missing = Path.Combine(_root, "gone").Replace("\\", "\\\\");
        File.WriteAllText(
            service.LocatorPath,
            "{\"configDirectory\":\"" + missing + "\"}",
            Encoding.UTF8);

        var resolution = service.Resolve();

        Assert.True(resolution.IsDefault);
        Assert.Equal("locator-directory-missing", resolution.Warning);
    }

    [Fact]
    public void UseDefaultDirectory_RemovesLocatorFile()
    {
        var service = CreateService();
        string custom = Path.Combine(_root, "custom");
        Directory.CreateDirectory(custom);
        service.SetCustomDirectory(custom);

        service.UseDefaultDirectory();

        Assert.False(File.Exists(service.LocatorPath));
        Assert.True(service.Resolve().IsDefault);
    }
}
