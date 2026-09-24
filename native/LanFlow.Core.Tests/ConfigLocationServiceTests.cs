using System.IO;
using System.Text;
using LanFlow.Desktop.Services;
using Xunit;

namespace LanFlow.Core.Tests;

public class ConfigLocationServiceTests : IDisposable
{
    private readonly string _root;

    public ConfigLocationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "lanflow-locator-" + Guid.NewGuid().ToString("N"));
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
        Assert.Equal(Path.Combine(_root, "LanFlow"), resolution.DirectoryPath);
        Assert.Equal(Path.Combine(_root, "LanFlow", "config.json"), resolution.ConfigPath);
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
