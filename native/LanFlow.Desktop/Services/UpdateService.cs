using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LanFlow.Desktop.Services;

// 一键更新：检测 GitHub 最新 Release、下载匹配通道（lite/full）的资产，并自更新重启。
// 通道识别：优先按同目录是否存在 WPF 原生 dll 判断（full 自包含），其次读 channel.txt，默认 lite。
public sealed class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/ZergZFZ/LanFlow/releases/latest";
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
        client.DefaultRequestHeaders.Add("User-Agent", "LanFlow-Updater");
        using var resp = await client.GetAsync(ApiUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;
        var latest = ParseVersion(tag);

        string? url = null;
        string? name = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            // 优先匹配当前通道的新格式资产
            foreach (var a in assets.EnumerateArray())
            {
                var n = a.GetProperty("name").GetString() ?? string.Empty;
                var u = a.GetProperty("browser_download_url").GetString() ?? string.Empty;
                var matched = CurrentChannel == "lite"
                    ? n.EndsWith("-lite.exe", StringComparison.OrdinalIgnoreCase)
                    : n.EndsWith("-full.zip", StringComparison.OrdinalIgnoreCase);
                if (matched)
                {
                    url = u;
                    name = n;
                    break;
                }
            }

            // 回退：lite 通道兼容旧的 -lite.zip（例如 1.3.3）
            if (url == null && CurrentChannel == "lite")
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var n = a.GetProperty("name").GetString() ?? string.Empty;
                    if (n.EndsWith("-lite.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        url = a.GetProperty("browser_download_url").GetString();
                        name = n;
                        break;
                    }
                }
            }
        }

        var hasUpdate = latest is not null && latest > CurrentVersion;
        return new UpdateInfo
        {
            HasUpdate = hasUpdate,
            LatestVersion = latest?.ToString(),
            CurrentVersion = CurrentVersion.ToString(3),
            DownloadUrl = url,
            AssetName = name,
            ReleaseNotes = body,
        };
    }

    public async Task DownloadAndApplyAsync(string downloadUrl, string assetName, IProgress<double>? progress, CancellationToken ct)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "LanFlowUpdate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var payloadDir = Path.Combine(workDir, "payload");
        Directory.CreateDirectory(payloadDir);
        var downloaded = Path.Combine(workDir, assetName);

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("User-Agent", "LanFlow-Updater");
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
                read += n;
                if (total > 0)
                {
                    progress?.Report((double)read / total);
                }
            }
        }

        // 整理成统一的可覆盖目录：lite 直接得到 LanFlow.exe，full 解压整目录。
        if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(downloaded, payloadDir, true);
        }
        else
        {
            File.Move(downloaded, Path.Combine(payloadDir, "LanFlow.exe"));
        }

        LaunchUpdater(payloadDir);

        // 终止当前进程，交由更新器等待本进程退出后覆盖并重启。
        Environment.Exit(0);
    }

    private static void LaunchUpdater(string payloadDir)
    {
        var bat = Path.Combine(Path.GetTempPath(), "LanFlowUpdater_" + Guid.NewGuid().ToString("N") + ".bat");
        var exeDir = AppContext.BaseDirectory.TrimEnd('\\');
        var pid = Process.GetCurrentProcess().Id;
        var content =
            "@echo off\r\n" +
            "set PID=" + pid + "\r\n" +
            "set SRC=\"" + payloadDir + "\"\r\n" +
            "set DST=\"" + exeDir + "\"\r\n" +
            ":wait\r\n" +
            "tasklist /fi \"PID eq %PID%\" 2>nul | find \"PID\" >nul\r\n" +
            "if %errorlevel%==0 (\r\n" +
            "  timeout /t 1 /nobreak >nul\r\n" +
            "  goto wait\r\n" +
            ")\r\n" +
            "xcopy /s /y /q \"%SRC%\\*\" \"%DST%\" >nul\r\n" +
            "start \"\" \"%DST%\\LanFlow.exe\"\r\n" +
            "rd /s /q \"%SRC%\"\r\n" +
            "del \"%~f0\"\r\n";
        File.WriteAllText(bat, content, System.Text.Encoding.ASCII);
        Process.Start(new ProcessStartInfo(bat) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
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
    public string? ReleaseNotes { get; init; }
}
