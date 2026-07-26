using System.IO;
using System.Text.Json;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _configDirectory;
    private readonly string _configPath;

    public ConfigStore()
    {
        _configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LanFlow");
        _configPath = Path.Combine(_configDirectory, "config.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            return Normalize(new AppConfig());
        }

        try
        {
            using var stream = File.OpenRead(_configPath);
            return Normalize(JsonSerializer.Deserialize<AppConfig>(stream, SerializerOptions) ?? new AppConfig());
        }
        catch (JsonException)
        {
            return Normalize(new AppConfig());
        }
        catch (IOException)
        {
            return Normalize(new AppConfig());
        }
    }

    private static AppConfig Normalize(AppConfig config)
    {
        config.Settings ??= new Settings();
        var settings = config.Settings;
        settings.Hotkey = string.IsNullOrWhiteSpace(settings.Hotkey) ? "Alt+Space" : settings.Hotkey;
        settings.Theme = settings.Theme == "light" ? "light" : "dark";
        settings.ThemeProfile = string.IsNullOrWhiteSpace(settings.ThemeProfile)
            ? (settings.Theme == "light" ? "浅色" : "深色")
            : settings.ThemeProfile;
        settings.ThemeColors ??= settings.Theme == "light" ? ThemeColors.Light() : ThemeColors.Dark();
        settings.CustomThemes ??= [];
        settings.LayoutMode = settings.LayoutMode == "card" ? "card" : "tile";
        settings.Opacity = Math.Clamp(settings.Opacity, 0.55, 1.0);
        settings.IconSize = Math.Clamp(settings.IconSize, 24, 72);
        settings.CardWidth = Math.Clamp(settings.CardWidth, 48, 320);
        settings.CardHeight = Math.Clamp(settings.CardHeight, 48, 240);
        settings.CardSize = Math.Clamp(settings.CardSize, 76, 160);
        settings.TextSize = Math.Clamp(settings.TextSize, 10, 18);
        settings.ItemSpacing = Math.Clamp(settings.ItemSpacing, 0, 64);
        settings.RowSpacing = Math.Clamp(settings.RowSpacing, 0, 80);
        settings.ContentPadding = Math.Clamp(settings.ContentPadding, 6, 40);
        settings.GroupLayout = settings.GroupLayout == "top" ? "top" : "left";
        return config;
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(_configDirectory);
        var temporaryPath = _configPath + ".tmp";
        var json = JsonSerializer.Serialize(config, SerializerOptions);

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _configPath, true);
    }
}
