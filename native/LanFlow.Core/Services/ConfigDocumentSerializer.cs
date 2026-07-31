using System.IO;
using System.Text.Json;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

/// <summary>
/// 配置文档的统一 UTF-8 序列化与反序列化，保证 ConfigStore 与迁移服务写出的格式一致。
/// </summary>
public static class ConfigDocumentSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static byte[] Serialize(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return JsonSerializer.SerializeToUtf8Bytes(config, Options);
    }

    public static AppConfig Deserialize(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return JsonSerializer.Deserialize<AppConfig>(stream, Options) ?? new AppConfig();
    }
}
