using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlickD.Desktop.Services;

/// <summary>
/// 配置目录解析结果。<paramref name="Warning"/> 使用稳定英文代码，由上层映射为 UI 文案。
/// </summary>
public sealed record ConfigLocationResolution(
    string DirectoryPath,
    string ConfigPath,
    bool IsDefault,
    string? Warning);

/// <summary>
/// 解析配置目录：默认 %APPDATA%\FlickD，可通过 locator 文件指向自定义目录。
/// 2.0.0 改名后旧默认目录为 %APPDATA%\LanFlow，首次启动时自动复制旧配置与 locator。
/// </summary>
public sealed class ConfigLocationService
{
    private static readonly JsonSerializerOptions LocatorJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private sealed record LocatorDocument(
        [property: JsonPropertyName("configDirectory")] string ConfigDirectory);

    public ConfigLocationService(string? applicationDataRoot = null)
    {
        string root = applicationDataRoot
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DefaultDirectory = NormalizeDirectory(Path.Combine(root, "FlickD"));
        LegacyDirectory = NormalizeDirectory(Path.Combine(root, "LanFlow"));
        LocatorPath = Path.Combine(DefaultDirectory, "config-location.json");
    }

    public string DefaultDirectory { get; }

    /// <summary>2.0.0 改名前的默认目录，仅用于一次性迁移。</summary>
    public string LegacyDirectory { get; }

    public string LocatorPath { get; }

    public ConfigLocationResolution Resolve()
    {
        MigrateLegacyDirectory();
        if (!File.Exists(LocatorPath))
        {
            return CreateDefault(warning: null);
        }

        try
        {
            LocatorDocument? document;
            using (var stream = File.OpenRead(LocatorPath))
            {
                document = JsonSerializer.Deserialize<LocatorDocument>(stream, LocatorJsonOptions);
            }

            if (document is null || string.IsNullOrWhiteSpace(document.ConfigDirectory))
            {
                return CreateDefault("locator-empty");
            }

            string directory = NormalizeDirectory(document.ConfigDirectory);
            if (!Directory.Exists(directory))
            {
                return CreateDefault("locator-directory-missing");
            }

            bool isDefault = PathsEqual(directory, DefaultDirectory);
            string selected = isDefault ? DefaultDirectory : directory;
            return new ConfigLocationResolution(
                selected,
                Path.Combine(selected, "config.json"),
                isDefault,
                Warning: null);
        }
        catch (Exception ex) when (ex is JsonException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return CreateDefault("locator-invalid");
        }
    }

    public void SetCustomDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Configuration directory is required.", nameof(directory));
        }

        string normalized = NormalizeDirectory(directory);
        if (!Directory.Exists(normalized))
        {
            throw new DirectoryNotFoundException(normalized);
        }

        if (PathsEqual(normalized, DefaultDirectory))
        {
            UseDefaultDirectory();
            return;
        }

        Directory.CreateDirectory(DefaultDirectory);
        string tempPath = LocatorPath + ".tmp";
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                new LocatorDocument(normalized),
                LocatorJsonOptions);
            File.WriteAllBytes(tempPath, bytes);

            using (var validationStream = File.OpenRead(tempPath))
            {
                LocatorDocument? validated = JsonSerializer.Deserialize<LocatorDocument>(
                    validationStream,
                    LocatorJsonOptions);
                if (validated is null || !PathsEqual(validated.ConfigDirectory, normalized))
                {
                    throw new InvalidDataException("Locator validation failed.");
                }
            }

            File.Move(tempPath, LocatorPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    public void UseDefaultDirectory()
    {
        DeleteIfExists(LocatorPath + ".tmp");
        if (File.Exists(LocatorPath))
        {
            File.Delete(LocatorPath);
        }
    }

    internal static string NormalizeDirectory(string directory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    internal static bool PathsEqual(string left, string right) =>
        StringComparer.OrdinalIgnoreCase.Equals(
            NormalizeDirectory(left),
            NormalizeDirectory(right));

    private ConfigLocationResolution CreateDefault(string? warning) =>
        new(
            DefaultDirectory,
            Path.Combine(DefaultDirectory, "config.json"),
            IsDefault: true,
            warning);

    /// <summary>
    /// 一次性迁移旧默认目录（%APPDATA%\LanFlow）到新目录（%APPDATA%\FlickD）。
    /// 只在目标文件缺失时复制，旧目录保持原样以便回退；复制失败不阻断启动。
    /// locator 优先迁移：自定义目录用户的配置位置只记录在 locator 里，漏迁会读到空配置。
    /// </summary>
    private void MigrateLegacyDirectory()
    {
        try
        {
            if (PathsEqual(DefaultDirectory, LegacyDirectory)) return;
            if (!Directory.Exists(LegacyDirectory)) return;

            string legacyLocator = Path.Combine(LegacyDirectory, "config-location.json");
            if (File.Exists(legacyLocator))
            {
                if (!File.Exists(LocatorPath))
                {
                    Directory.CreateDirectory(DefaultDirectory);
                    File.Copy(legacyLocator, LocatorPath, overwrite: false);
                }

                return;
            }

            string legacyConfig = Path.Combine(LegacyDirectory, "config.json");
            string defaultConfig = Path.Combine(DefaultDirectory, "config.json");
            if (File.Exists(legacyConfig) && !File.Exists(defaultConfig))
            {
                Directory.CreateDirectory(DefaultDirectory);
                File.Copy(legacyConfig, defaultConfig, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            // 迁移失败时按默认目录继续，旧数据保持原样，不影响启动。
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
