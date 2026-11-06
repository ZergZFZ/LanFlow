using System;
using System.IO;

namespace LanFlow.Desktop.Services.Ocr;

public enum OcrBundleStatus
{
    NotInstalled,
    Installed,
    Corrupted,
}

public sealed record OcrBundleLocation(
    OcrBundleStatus Status,
    string RootDirectory,
    OcrManifest? Manifest,
    System.Collections.Generic.IReadOnlyList<string> Problems);

/// <summary>
/// 定位 OCR bundle：优先用户目录（下载/导入），其次应用目录（full 包内置）。
/// </summary>
public static class OcrBundleLocator
{
    public static string UserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanFlow", "ocr");

    public static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "ocr");

    /// <summary>
    /// 只读检查，不加载程序集、不联网。调用方应在用户主动使用取字时才调用。
    /// </summary>
    public static OcrBundleLocation Locate()
    {
        foreach (var directory in new[] { UserDirectory, BundledDirectory })
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            var manifest = OcrManifest.Load(manifestPath);
            var problems = OcrManifestVerifier.Verify(directory, manifest);
            return new OcrBundleLocation(
                problems.Count == 0 ? OcrBundleStatus.Installed : OcrBundleStatus.Corrupted,
                directory,
                manifest,
                problems);
        }

        return new OcrBundleLocation(OcrBundleStatus.NotInstalled, UserDirectory, null, []);
    }
}
