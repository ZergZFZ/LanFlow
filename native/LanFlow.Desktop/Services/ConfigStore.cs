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
            return new AppConfig();
        }

        try
        {
            using var stream = File.OpenRead(_configPath);
            return JsonSerializer.Deserialize<AppConfig>(stream, SerializerOptions) ?? new AppConfig();
        }
        catch (JsonException)
        {
            return new AppConfig();
        }
        catch (IOException)
        {
            return new AppConfig();
        }
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
