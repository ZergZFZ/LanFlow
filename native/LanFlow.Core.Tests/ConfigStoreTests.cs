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

    [Fact]
    public void Load_LegacyOpacityMigratesToWholeWindowWithoutChangingValue()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, "config.json"), """
            { "settings": { "opacity": 0.72, "layoutMode": "tile" }, "groups": [] }
            """);

        var settings = new ConfigStore("Alt+Space", _tempDirectory).Load().Settings;

        Assert.Equal(SettingsOptionValues.TransparencyWholeWindow, settings.TransparencyMode);
        Assert.Equal(0.72, settings.WholeWindowOpacity, 3);
        Assert.Equal(0.85, settings.LayeredOpacity, 3);
        Assert.Equal(SettingsOptionValues.GridLayout, settings.LayoutMode);
    }

    [Fact]
    public void Load_MissingFileUsesLayeredEightyFivePercentDefaults()
    {
        var settings = new ConfigStore("Alt+Space", _tempDirectory).Load().Settings;

        Assert.Equal(SettingsOptionValues.TransparencyLayered, settings.TransparencyMode);
        Assert.Equal(0.85, settings.LayeredOpacity, 3);
        Assert.Equal(0.85, settings.WholeWindowOpacity, 3);
    }

    [Fact]
    public void Load_ClampsNewVisualSettings()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, "config.json"), """
            { "settings": {
                "groupLabelSize": 2,
                "groupLabelFontSize": 99,
                "groupNavigationWidth": 999,
                "layeredOpacity": 0.1,
                "wholeWindowOpacity": 2.0
            }, "groups": [] }
            """);

        var settings = new ConfigStore("Alt+Space", _tempDirectory).Load().Settings;

        Assert.Equal(28, settings.GroupLabelSize);
        Assert.Equal(18, settings.GroupLabelFontSize);
        Assert.Equal(280, settings.GroupNavigationWidth);
        Assert.Equal(0.40, settings.LayeredOpacity, 3);
        Assert.Equal(1.00, settings.WholeWindowOpacity, 3);
    }
    [Theory]
    [InlineData("grid", "grid")]
    [InlineData("card", "card")]
    [InlineData("tile", "grid")]
    [InlineData("list", "grid")]
    [InlineData("", "grid")]
    [InlineData("unknown-mode", "grid")]
    public void Load_NormalizesSupportedAndLegacyLayoutModes(string input, string expected)
    {
        File.WriteAllText(
            Path.Combine(_tempDirectory, "config.json"),
            "{ \"settings\": { \"layoutMode\": \"" + input + "\" }, \"groups\": [] }");

        var layoutMode = new ConfigStore("Alt+Space", _tempDirectory).Load().Settings.LayoutMode;

        Assert.Equal(expected, layoutMode);
    }

    [Theory]
    [InlineData(250, 250)]
    [InlineData(0, 0)]
    [InlineData(500, 500)]
    [InlineData(-10, 0)]
    public void Load_ClampsGroupHoverDelayMsToValidRange(int input, int expected)
    {
        File.WriteAllText(
            Path.Combine(_tempDirectory, "config.json"),
            "{ \"settings\": { \"groupHoverDelayMs\": " + input + " }, \"groups\": [] }");

        var groupHoverDelayMs = new ConfigStore("Alt+Space", _tempDirectory).Load().Settings.GroupHoverDelayMs;

        Assert.Equal(expected, groupHoverDelayMs);
    }

    [Fact]
    public void Load_DefaultsGroupHoverDelayMsWhenMissing()
    {
        File.WriteAllText(
            Path.Combine(_tempDirectory, "config.json"),
            "{ \"settings\": {}, \"groups\": [] }");

        var groupHoverDelayMs = new ConfigStore("Alt+Space", _tempDirectory).Load().Settings.GroupHoverDelayMs;

        Assert.Equal(SettingsOptionValues.DefaultGroupHoverDelayMs, groupHoverDelayMs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
    }
}
