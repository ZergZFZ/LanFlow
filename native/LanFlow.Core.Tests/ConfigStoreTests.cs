using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "LanFlow.ConfigStoreTests", Guid.NewGuid().ToString("N"));

    public ConfigStoreTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public void Save_ReplacesTheWholeConfigurationAndLeavesNoTemporaryFile()
    {
        var store = new ConfigStore("Alt+Space", _tempDirectory);
        store.Save(new AppConfig { Groups = [new Group { Id = "first", Name = "旧分组" }] });

        store.Save(new AppConfig
        {
            Groups = [new Group { Id = "second", Name = "新分组" }],
            Settings = new Settings { Hotkey = "Ctrl+Shift+L" },
        });

        var loaded = store.Load();
        Assert.Equal("second", Assert.Single(loaded.Groups).Id);
        Assert.Equal("新分组", loaded.Groups[0].Name);
        Assert.Equal("Ctrl+Shift+L", loaded.Settings.Hotkey);
        Assert.False(File.Exists(store.ConfigPath + ".tmp"));
        Assert.Equal([0x7B], File.ReadAllBytes(store.ConfigPath).Take(1).ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
    }
}