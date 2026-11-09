using System.Text.Json;
using FlickD.Desktop.Models;

namespace FlickD.Desktop.Services;

public static class SettingsComparer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static bool Equals(Settings left, Settings right) =>
        StringComparer.Ordinal.Equals(
            JsonSerializer.Serialize(left, Options),
            JsonSerializer.Serialize(right, Options));
}
