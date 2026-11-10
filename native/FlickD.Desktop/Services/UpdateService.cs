using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FlickD.Desktop.Services;

// 一键更新：检测 GitHub 最新 Release，用 GitHub 提供的 SHA-256 摘要校验资产，
// 下载匹配通道（lite/full）后自更新重启。
// 通道识别：优先按同目录是否存在 WPF 原生 dll 判断（full 自包含），其次读 channel.txt，默认 lite。
public sealed class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/ZergZFZ/LanFlow/releases/latest";

    public const string PrimaryExeName = "FlickD.exe";
    private static readonly string[] FullMarkers = { "PresentationNative_cor3.dll", "wpfgfx_cor3.dll", "vcruntime140_cor3.dll" };

    public static string CurrentChannel
    {
        get
        {
            var baseDir = AppContext.BaseDirectory;
            if (FullMarkers.Any(m => File.Exists(Path.Combine(baseDir, m))))
            {
                return "full";
            }

            try
            {
                var c = File.ReadAllText(Path.Combine(baseDir, "channel.txt")).Trim().ToLowerInvariant();
                if (c is "lite" or "full")
                {
                    return c;
                }
            }
            catch
            {
                // 无标记文件时回退 lite
            }

            return "lite";
        }
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent", "FlickD-Updater");
        using var resp = await client.GetAsync(ApiUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;
        var latest = ParseVersion(tag);
        if (latest is null)
        {
            return new UpdateInfo
            {
                HasUpdate = false,
                CurrentVersion = CurrentVersion.ToString(3),
                Error = $"无法解析最新版本号：{tag}",
            };
        }

        var asset = SelectAsset(root, CurrentChannel);
        return new UpdateInfo
        {
            HasUpdate = latest > CurrentVersion,
            LatestVersion = latest.ToString(),
            CurrentVersion = CurrentVersion.ToString(3),
            DownloadUrl = asset.Url,
            AssetName = asset.Name,
            AssetSize = asset.Size,
            AssetSha256 = asset.Sha256,
            ReleaseNotes = body,
        };
    }

    // 纯函数：从 Release assets 中挑选当前通道的资产，并提取 GitHub 返回的 SHA-256 digest。
    // 不访问网络、不读文件系统，便于回归测试。
    public static (string? Url, string? Name, long? Size, string? Sha256) SelectAsset(JsonElement root, string channel)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return (null, null, null, null);
        }

        var suffix = channel == "lite" ? "-lite.exe" : "-full.zip";
        foreach (var a in assets.EnumerateArray())
        {
            var name = ReadString(a, "name");
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            return (ReadString(a, "browser_download_url"), name, ReadLong(a, "size"), ParseDigest(a));
        }

        // 回退：lite 通道兼容旧的 -lite.zip（例如 1.3.3）
        if (channel == "lite")
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = ReadString(a, "name");
                if (!name.EndsWith("-lite.zip", StringComparison.OrdinalIgnoreCase)) continue;
                return (ReadString(a, "browser_download_url"), name, ReadLong(a, "size"), ParseDigest(a));
            }
        }

        return (null, null, null, null);
    }

    private static string ReadString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object &&
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static long? ReadLong(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object &&
        e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : null;

    // GitHub Release API 为每个资产返回 digest: "sha256:<64 hex>"。
    public static string? ParseDigest(JsonElement asset) => NormalizeSha256(ReadString(asset, "digest"));

    // 接受 "sha256:<hex>" 或裸 64 位 hex；其他一律视为无效（含缺失）。
    public static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim();
        if (s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) s = s["sha256:".Length..];
        if (s.Length != 64) return null;
        return s.All(Uri.IsHexDigit) ? s.ToUpperInvariant() : null;
    }

    public async Task DownloadAndApplyAsync(
        string downloadUrl,
        string assetName,
        long? expectedSize,
        string? expectedSha256,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "FlickDUpdate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var payloadDir = Path.Combine(workDir, "payload");
        Directory.CreateDirectory(payloadDir);
        var downloaded = Path.Combine(workDir, assetName);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.Add("User-Agent", "FlickD-Updater");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var r = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                r.EnsureSuccessStatusCode();
                var total = r.Content.Headers.ContentLength ?? -1L;
                await using var src = await r.Content.ReadAsStreamAsync(ct);
                await using var fs = File.Create(downloaded);
                var buf = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, n), ct);
                    hash.AppendData(buf.AsSpan(0, n));
                    read += n;
                    if (total > 0)
                    {
                        progress?.Report((double)read / total);
                    }
                }

                VerifyDownloadedIntegrity(expectedSha256, expectedSize,
                    Convert.ToHexString(hash.GetHashAndReset()), read);
            }

            // 整理成统一的可覆盖目录：lite 直接得到 FlickD.exe，full 解压整目录。
            if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(downloaded, payloadDir, true);
                // full 包可达 ~90 MB，解压后立刻删掉下载副本，避免每次更新在 %TEMP% 堆积。
                File.Delete(downloaded);
            }
            else
            {
                File.Move(downloaded, Path.Combine(payloadDir, PrimaryExeName));
            }

            if (!IsValidPayload(payloadDir))
            {
                throw new InvalidDataException($"下载的更新包缺少有效的主程序（{PrimaryExeName}），已取消更新。");
            }

            LaunchUpdater(payloadDir, workDir);

            // 终止当前进程，交由更新器等待本进程退出后覆盖并重启。
            Environment.Exit(0);
        }
        catch
        {
            TryDeleteDirectory(workDir);
            throw;
        }
    }

    // 下载完整性闸门：先比对字节数，再比对 GitHub digest。
    // 缺少 digest 时拒绝覆盖安装目录——宁可让用户手动下载，也不能把来源不明的包写进程序目录。
    public static void VerifyDownloadedIntegrity(
        string? expectedSha256, long? expectedSize, string actualSha256, long actualSize)
    {
        EnsureDownloadedSize(expectedSize, actualSize);
        var expected = NormalizeSha256(expectedSha256);
        if (expected is null)
        {
            throw new InvalidDataException(
                "更新源未提供有效的 SHA-256 摘要，无法确认下载完整性，已取消更新。请前往开源地址手动下载。");
        }

        if (!string.Equals(expected, actualSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"下载文件 SHA-256 校验失败（预期 {expected}，实际 {actualSha256}），已取消更新。请重试或手动下载。");
        }
    }

    public static void EnsureDownloadedSize(long? expectedSize, long actualSize)
    {
        if (expectedSize is null || expectedSize.Value < 0) return;
        if (actualSize != expectedSize.Value)
        {
            throw new InvalidDataException(
                $"下载文件大小不符：预期 {expectedSize.Value} 字节，实际 {actualSize} 字节，已取消更新。");
        }
    }

    // 覆盖前验证 payload 中确实存在可执行的主程序。
    // 2.1.0 起不再发布 LanFlow.exe 兼容壳，只认 FlickD.exe。
    public static bool IsValidPayload(string payloadDir) =>
        Directory.Exists(payloadDir) && HasValidExecutable(Path.Combine(payloadDir, PrimaryExeName));

    private static bool HasValidExecutable(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
            // 清理失败不掩盖原始异常。
        }
    }

    private static void LaunchUpdater(string payloadDir, string workDir)
    {
        var bat = Path.Combine(Path.GetTempPath(), "FlickDUpdater_" + Guid.NewGuid().ToString("N") + ".bat");
        var script = BuildUpdaterScript(payloadDir, AppContext.BaseDirectory.TrimEnd('\\'),
            Process.GetCurrentProcess().Id, workDir);
        // 无 BOM UTF-8 + chcp 65001：纯 ASCII 路径行为不变，中文安装目录也有机会正确解析。
        File.WriteAllText(bat, script, new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo(bat) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
    }

    // 覆盖脚本抽成纯函数，便于回归测试锁定「完整备份 / 失败回滚 / 单主程序名 / 清理临时目录」契约。
    // workDir 为下载时创建的临时父目录；为空时退化为只删 payload，兼容旧调用与测试。
    public static string BuildUpdaterScript(string payloadDir, string exeDir, int pid, string? workDir = null)
    {
        var backup = Path.Combine(Path.GetTempPath(), "FlickDBackup_" + Guid.NewGuid().ToString("N"));
        var sb = new StringBuilder();
        sb.Append("@echo off\r\n");
        sb.Append("chcp 65001 >nul\r\n");
        sb.Append("set \"PID=").Append(pid).Append("\"\r\n");
        sb.Append("set \"SRC=").Append(payloadDir).Append("\"\r\n");
        sb.Append("set \"DST=").Append(exeDir).Append("\"\r\n");
        sb.Append("set \"BACKUP=").Append(backup).Append("\"\r\n");
        sb.Append("set \"WORK=").Append(workDir ?? payloadDir).Append("\"\r\n");
        sb.Append(":wait\r\n");
        sb.Append("tasklist /fi \"PID eq %PID%\" 2>nul | find \"PID\" >nul\r\n");
        sb.Append("if %errorlevel%==0 (\r\n");
        sb.Append("  timeout /t 1 /nobreak >nul\r\n");
        sb.Append("  goto wait\r\n");
        sb.Append(")\r\n");
        sb.Append("rem 覆盖前完整备份安装目录，任何失败都整体回滚，避免半新半旧的依赖组合\r\n");
        sb.Append("if not exist \"%BACKUP%\" mkdir \"%BACKUP%\"\r\n");
        sb.Append("xcopy /s /y /q \"%DST%\\*\" \"%BACKUP%\" >nul\r\n");
        sb.Append("if errorlevel 1 goto backupfailed\r\n");
        sb.Append("xcopy /s /y /q \"%SRC%\\*\" \"%DST%\" >nul\r\n");
        sb.Append("if errorlevel 1 goto rollback\r\n");
        sb.Append("start \"\" \"%DST%\\").Append(PrimaryExeName).Append("\"\r\n");
        sb.Append("timeout /t 5 /nobreak >nul\r\n");
        sb.Append("tasklist /fi \"IMAGENAME eq ").Append(PrimaryExeName).Append("\" 2>nul | find \"")
          .Append(PrimaryExeName).Append("\" >nul\r\n");
        sb.Append("if %errorlevel%==1 goto rollback\r\n");
        sb.Append("goto cleanup\r\n");
        sb.Append(":rollback\r\n");
        sb.Append("xcopy /s /y /q \"%BACKUP%\\*\" \"%DST%\" >nul\r\n");
        sb.Append("if errorlevel 1 goto keepbackup\r\n");
        sb.Append("start \"\" \"%DST%\\").Append(PrimaryExeName).Append("\"\r\n");
        sb.Append("goto cleanup\r\n");
        sb.Append(":backupfailed\r\n");
        sb.Append("echo FlickD 更新备份失败，未覆盖安装目录 > \"%TEMP%\\FlickDUpdateFailure.txt\"\r\n");
        sb.Append("exit /b 1\r\n");
        sb.Append(":keepbackup\r\n");
        sb.Append("echo FlickD 更新回滚失败，请手动从备份恢复 > \"%TEMP%\\FlickDUpdateFailure.txt\"\r\n");
        sb.Append("echo 备份已保留: %BACKUP% >> \"%TEMP%\\FlickDUpdateFailure.txt\"\r\n");
        sb.Append("exit /b 1\r\n");
        sb.Append(":cleanup\r\n");
        sb.Append("rd /s /q \"%WORK%\"\r\n");
        sb.Append("rd /s /q \"%BACKUP%\"\r\n");
        sb.Append("del \"%~f0\"\r\n");
        return sb.ToString();
    }

    private static Version? ParseVersion(string tag)
    {
        var s = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : null;
    }
}

public sealed class UpdateInfo
{
    public bool HasUpdate { get; init; }
    public string? LatestVersion { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public string? DownloadUrl { get; init; }
    public string? AssetName { get; init; }
    public long? AssetSize { get; init; }
    public string? AssetSha256 { get; init; }
    public string? ReleaseNotes { get; init; }
    public string? Error { get; init; }
}
