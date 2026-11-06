using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanFlow.Desktop.Services.Ocr;

/// <summary>
/// OCR bundle 完整性清单。manifest.json 为唯一权威，逐文件记录 SHA-256 与大小。
/// </summary>
public sealed class OcrManifest
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [JsonPropertyName("bundleId")]
    public string BundleId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("minAppVersion")]
    public string MinAppVersion { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public List<OcrManifestFile> Files { get; set; } = [];

    public static OcrManifest? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<OcrManifest>(File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class OcrManifestFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
