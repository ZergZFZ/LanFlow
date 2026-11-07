using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LanFlow.Desktop.Services.Ocr;

/// <summary>
/// OCR bundle 的下载、校验、安装与卸载。仅在用户主动操作时调用，不在启动时执行。
/// </summary>
public sealed class OcrBundleService
{
    private const string ReleaseApiUrl = "https://api.github.com/repos/ZergZFZ/LanFlow/releases/latest";
    private const string AssetSuffix = "-ocr.zip";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // UA 随程序集版本走，避免每个版本硬编码（P3-1）。
        var version = UpdateService.CurrentVersion.ToString(3);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LanFlow", version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<string> DownloadLatestAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"lanflow-ocr-{Guid.NewGuid():N}.zip");

        try
        {
            using var releaseResponse = await Http.GetAsync(ReleaseApiUrl, cancellationToken).ConfigureAwait(false);
            if (!releaseResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"查询发布信息失败：HTTP {(int)releaseResponse.StatusCode}");
            }

            var payload = await releaseResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var assetUrl = FindAssetUrl(payload);
            if (assetUrl is null)
            {
                throw new InvalidOperationException("最新版本未提供 OCR 组件包（*-ocr.zip）");
            }

            using var downloadResponse = await Http
                .GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            downloadResponse.EnsureSuccessStatusCode();

            var contentLength = downloadResponse.Content.Headers.ContentLength ?? -1;
            await using (var source = await downloadResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(tempZip))
            {
                var buffer = new byte[81920];
                var totalRead = 0L;
                var stopwatch = Stopwatch.StartNew();
                var lastReport = 0L;

                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    totalRead += read;

                    if (progress is not null && contentLength > 0 && (stopwatch.ElapsedMilliseconds - lastReport > 100 || totalRead == contentLength))
                    {
                        lastReport = stopwatch.ElapsedMilliseconds;
                        progress.Report(Math.Min(1.0, (double)totalRead / contentLength));
                    }
                }

                progress?.Report(1.0);
            }

            return tempZip;
        }
        catch
        {
            TryDeleteFile(tempZip);
            throw;
        }
    }

    private static string? FindAssetUrl(string releaseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(releaseJson);
            if (!document.RootElement.TryGetProperty("assets", out var assets)) return null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is null) continue;
                if (!name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                return asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() : null;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// 从 zip 安装：解压 → manifest 校验 → 原子替换到用户目录。
    /// </summary>
    public async Task<string> InstallFromZipAsync(string zipPath, CancellationToken cancellationToken)
    {
        var staging = Path.Combine(Path.GetTempPath(), $"lanflow-ocr-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);

        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true), cancellationToken)
                .ConfigureAwait(false);

            // 兼容 zip 内多包一层 ocr/ 目录的情况。
            var root = staging;
            if (!File.Exists(Path.Combine(root, "manifest.json")))
            {
                var nested = Directory.GetDirectories(staging)
                    .FirstOrDefault(d => File.Exists(Path.Combine(d, "manifest.json")));
                if (nested is not null) root = nested;
            }

            var manifest = OcrManifest.Load(Path.Combine(root, "manifest.json"));
            var problems = OcrManifestVerifier.Verify(root, manifest);
            if (problems.Count > 0)
            {
                throw new InvalidDataException("OCR 组件校验失败：" + string.Join("；", problems.Take(3)));
            }

            var target = OcrBundleLocator.UserDirectory;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            // rename-swap：先把旧目录改名腾位，再把新目录移入。引擎加载后
            // onnxruntime.dll 等原生库已映射进进程，删除会失败，但改名总是成功；
            // 残留的 .old-* 目录由 SweepStaleBackups 在下次安装/定位时惰性清理。
            var backup = target + $".old-{Guid.NewGuid():N}";
            var swapped = false;
            if (Directory.Exists(target))
            {
                try
                {
                    Directory.Move(target, backup);
                    swapped = true;
                }
                catch (IOException)
                {
                    // 同卷改名都失败（目录被句柄引用等）：退回原位并抛错，不破坏现场。
                    throw;
                }
            }

            try
            {
                MoveOrCopy(root, target);
            }
            catch
            {
                if (swapped && Directory.Exists(backup) && !Directory.Exists(target))
                {
                    Directory.Move(backup, target);
                }
                throw;
            }

            if (swapped)
            {
                TryDeleteDirectory(backup);
            }

            SweepStaleBackups(target);
            OcrBundleLocator.Invalidate();
            return target;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public void Uninstall()
    {
        var target = OcrBundleLocator.UserDirectory;
        if (!Directory.Exists(target)) return;

        foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* ignore */ }
        }
        TryDeleteDirectory(target);
        SweepStaleBackups(target);
        OcrBundleLocator.Invalidate();
    }

    /// <summary>
    /// 惰性清理历史重装残留的 <c>ocr.old-*</c> 目录（引擎 DLL 被锁定时删除失败的遗留）。
    /// 仅在用户主动安装/卸载时调用，不违反「启动不触碰 OCR 资源」原则。
    /// </summary>
    internal static void SweepStaleBackups(string target)
    {
        var parent = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;

        var prefix = Path.GetFileName(target) + ".old-";
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(parent, prefix + "*"))
            {
                TryDeleteDirectory(directory);
            }
        }
        catch
        {
            // 清理失败不影响安装/卸载结果，残留留给下一次机会。
        }
    }

    /// <summary>
    /// 同卷优先重命名；临时目录与用户目录跨卷时退化为复制（TEMP 可被用户改到其他盘）。
    /// </summary>
    private static void MoveOrCopy(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException)
        {
            CopyDirectory(source, destination);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* ignore */ }
    }
}
