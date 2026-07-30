using System.IO;

namespace LanFlow.Desktop.Services;

public readonly record struct IconCacheKey(
    string Identity,
    int PixelSize,
    long VersionStamp,
    string ThemeVariant)
{
    public static IconCacheKey Create(string path, int pixelSize, long versionStamp, string themeVariant) =>
        new(Path.GetFullPath(path).ToUpperInvariant(), pixelSize, versionStamp, themeVariant);
}
