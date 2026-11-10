using System;
using System.IO;

namespace FlickD.Desktop.Services.Ocr;

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
/// 定位 OCR bundle：优先用户目录（下载/导入），其次应用目录。
/// 2.1.0 起 full 包不再内置 ocr/；保留应用目录查找只为让 2.0.x 老安装升级后
/// 仍能使用其目录中已内置的组件，新安装一律走用户目录。
/// Locate 内部对整个 bundle 逐文件 SHA-256（约 22MB），按 T 取字路径上曾重复执行
/// 两次；这里按「目录 + 写入时间戳」缓存结果，安装/导入/卸载/引擎重置时失效。
/// 只在取字路径调用，不违反 1.6.0「启动阶段不触碰 OCR 资源」原则。
/// </summary>
public static class OcrBundleLocator
{
    private static readonly object Gate = new();
    private static string? _cachedDirectory;
    private static DateTimeOffset _cachedStamp;
    private static OcrBundleLocation? _cached;

    public static string UserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlickD", "ocr");

    public static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "ocr");

    /// <summary>
    /// 只读检查，不加载程序集、不联网。调用方应在用户主动使用取字时才调用。
    /// 查找顺序：用户目录 → 应用内置目录。
    /// 2.0.0 更名后旧组件（LanFlow.Ocr.dll）引用的 LanFlow.Core 已不存在，
    /// 二进制不兼容，故不把 %APPDATA%\LanFlow\ocr 纳入查找，改为提示重新下载。
    /// </summary>
    public static OcrBundleLocation Locate()
    {
        lock (Gate)
        {
            foreach (var directory in new[] { UserDirectory, BundledDirectory })
            {
                var manifestPath = Path.Combine(directory, "manifest.json");
                if (!File.Exists(manifestPath)) continue;

                // 缓存键：目录 + manifest 修改时间。安装/卸载都会触碰 manifest，
                // 时间戳变化即视为失效；同时间戳则直接复用上次校验结论。
                var stamp = TryGetStamp(manifestPath);
                if (_cached is not null &&
                    string.Equals(_cachedDirectory, directory, StringComparison.OrdinalIgnoreCase) &&
                    _cachedStamp == stamp)
                {
                    return _cached;
                }

                var manifest = OcrManifest.Load(manifestPath);
                var problems = OcrManifestVerifier.Verify(directory, manifest);
                var location = new OcrBundleLocation(
                    problems.Count == 0 ? OcrBundleStatus.Installed : OcrBundleStatus.Corrupted,
                    directory,
                    manifest,
                    problems);

                _cachedDirectory = directory;
                _cachedStamp = stamp;
                _cached = location;
                return location;
            }

            return new OcrBundleLocation(OcrBundleStatus.NotInstalled, UserDirectory, null, []);
        }
    }

    /// <summary>安装/导入/卸载/引擎重置后调用：丢弃缓存结论，下次 Locate 重新校验。</summary>
    public static void Invalidate()
    {
        lock (Gate)
        {
            _cached = null;
            _cachedDirectory = null;
            _cachedStamp = default;
        }
    }

    private static DateTimeOffset TryGetStamp(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTimeOffset.MinValue;
        }
    }
}
