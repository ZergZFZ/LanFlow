using System.Text.Json;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;
using LanFlow.Desktop.ViewModels;

namespace LanFlow.Core.Tests;

public sealed class ConfigStoreTests
{
    [Fact]
    public void Load_MigratesLegacyConfigAndAppliesSharedNormalization()
    {
        using var sandbox = new ConfigSandbox();
        File.WriteAllText(sandbox.ConfigPath, """
        {
          "version": 0,
          "settings": {
            "hotkey": "Alt+Space",
            "theme": "neon",
            "themeProfile": "",
            "themeColors": null,
            "customThemes": null,
            "opacity": 5.0,
            "layeredOpacity": 0.1,
            "wholeWindowOpacity": 9.0,
            "iconSize": 999,
            "groupHoverDelayMs": -300,
            "layoutMode": "bogus",
            "groupLayout": "diagonal",
            "groupSwitchMode": "??",
            "animationMode": "??",
            "transparencyMode": "??"
          }
        }
        """);

        var config = new ConfigStore("Ctrl+Alt+L").Load();

        Assert.Equal(1, config.Version);
        Assert.NotNull(config.Groups);
        Assert.Equal("Ctrl+Alt+L", config.Settings.Hotkey);
        Assert.Equal("dark", config.Settings.Theme);
        Assert.Equal("深色", config.Settings.ThemeProfile);
        Assert.NotNull(config.Settings.ThemeColors);
        Assert.NotNull(config.Settings.CustomThemes);
        Assert.Equal(0.55, config.Settings.LayeredOpacity);
        Assert.Equal(1.0, config.Settings.WholeWindowOpacity);
        // 分层模式下 Opacity 派生自 LayeredOpacity，而不是沿用旧值 5.0。
        Assert.Equal(0.55, config.Settings.Opacity);
        Assert.Equal(72, config.Settings.IconSize);
        Assert.Equal(0, config.Settings.GroupHoverDelayMs);
        Assert.Equal("tile", config.Settings.LayoutMode);
        Assert.Equal("left", config.Settings.GroupLayout);
        Assert.Equal("click", config.Settings.GroupSwitchMode);
        Assert.Equal("on", config.Settings.AnimationMode);
        Assert.Equal("layered", config.Settings.TransparencyMode);
    }

    [Fact]
    public void Load_KeepsExplicitHotkeyButFillsEmptyOneWithPlatformDefault()
    {
        using var sandbox = new ConfigSandbox();
        var store = new ConfigStore("Ctrl+Alt+L");

        File.WriteAllText(sandbox.ConfigPath, """{"settings":{"hotkey":"Ctrl+Alt+K"}}""");
        Assert.Equal("Ctrl+Alt+K", store.Load().Settings.Hotkey);

        File.WriteAllText(sandbox.ConfigPath, """{"settings":{"hotkey":"   "}}""");
        Assert.Equal("Ctrl+Alt+L", store.Load().Settings.Hotkey);
    }

    [Fact]
    public void Save_WritesAtomicJsonAndRoundTrips()
    {
        using var sandbox = new ConfigSandbox();
        var store = new ConfigStore("Ctrl+Alt+L");
        var config = new AppConfig();
        config.Settings.Hotkey = "Ctrl+Alt+K";
        config.Groups.Add(new Group { Name = "开发" });

        store.Save(config);

        Assert.True(File.Exists(sandbox.ConfigPath));
        Assert.False(File.Exists(sandbox.ConfigPath + ".tmp"));
        using (var document = JsonDocument.Parse(File.ReadAllText(sandbox.ConfigPath)))
        {
            Assert.Equal(0, document.RootElement.GetProperty("version").GetInt32());
        }

        var loaded = store.Load();
        Assert.Equal(1, loaded.Version);
        Assert.Equal("Ctrl+Alt+K", loaded.Settings.Hotkey);
        Assert.Single(loaded.Groups);
        Assert.Equal("开发", loaded.Groups[0].Name);
    }

    [Fact]
    public void ApplyAppearance_AndConfigStoreLoad_ShareTheSameClampRules()
    {
        var config = new AppConfig();
        var store = new RecordingStore(config);
        var viewModel = new MainViewModel(store);
        store.Reset();

        var source = config.Settings.Clone();
        source.IconSize = 999;
        source.GroupHoverDelayMs = 5000;
        source.WholeWindowOpacity = 0.1;
        source.TransparencyMode = "wholeWindow";

        viewModel.ApplyAppearance(source, persist: false);

        Assert.Equal(72, viewModel.Settings.IconSize);
        Assert.Equal(1000, viewModel.Settings.GroupHoverDelayMs);
        Assert.Equal(0.55, viewModel.Settings.WholeWindowOpacity);
        Assert.Equal(0.55, viewModel.Settings.Opacity);
    }
}
