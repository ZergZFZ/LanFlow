using System;
using System.IO;
using System.Text.Json;
using FlickD.Desktop.Services;
using Xunit;

namespace FlickD.Desktop.Tests;

public sealed class UpdateServiceTests
{
    private static readonly string Hash = new string('A', 64);
    private static readonly string LowerHash = new string('a', 64);
    private static readonly string OtherHash = new string('B', 64);

    private static JsonElement Release(string assetsJson)
    {
        using var doc = JsonDocument.Parse("{\"tag_name\":\"v2.1.0\",\"assets\":" + assetsJson + "}");
        return doc.RootElement.Clone();
    }

    private sealed class TempDir : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "FlickDTests_" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Root);

        public void Dispose()
        {
            try { Directory.Delete(Root, true); }
            catch { /* 清理失败不影响断言 */ }
        }
    }

    [Fact]
    public void SelectAsset_PicksFullChannelAsset_WithDigestAndSize()
    {
        var root = Release("[" +
            "{\"name\":\"FlickD-2.1.0-lite.exe\",\"browser_download_url\":\"https://x/lite\",\"size\":10,\"digest\":\"sha256:" + LowerHash + "\"}," +
            "{\"name\":\"FlickD-2.1.0-full.zip\",\"browser_download_url\":\"https://x/full\",\"size\":20,\"digest\":\"sha256:" + Hash + "\"}," +
            "{\"name\":\"FlickD-2.1.0-ocr.zip\",\"browser_download_url\":\"https://x/ocr\"}]");

        var (url, name, size, sha) = UpdateService.SelectAsset(root, "full");

        Assert.Equal("https://x/full", url);
        Assert.Equal("FlickD-2.1.0-full.zip", name);
        Assert.Equal(20L, size!.Value);
        Assert.Equal(Hash, sha);
    }

    [Fact]
    public void SelectAsset_PicksLiteChannelAsset_AndNormalizesDigest()
    {
        var root = Release("[" +
            "{\"name\":\"FlickD-2.1.0-full.zip\",\"browser_download_url\":\"https://x/full\"}," +
            "{\"name\":\"FlickD-2.1.0-lite.exe\",\"browser_download_url\":\"https://x/lite\",\"size\":7,\"digest\":\"sha256:" + LowerHash + "\"}]");

        var (url, name, size, sha) = UpdateService.SelectAsset(root, "lite");

        Assert.Equal("https://x/lite", url);
        Assert.Equal("FlickD-2.1.0-lite.exe", name);
        Assert.Equal(7L, size!.Value);
        Assert.Equal(Hash, sha);
    }

    [Fact]
    public void SelectAsset_FallsBackToLegacyLiteZip()
    {
        var root = Release("[{\"name\":\"FlickD-1.3.3-lite.zip\",\"browser_download_url\":\"https://x/old\",\"size\":3}]");

        var (url, name, _, _) = UpdateService.SelectAsset(root, "lite");

        Assert.Equal("https://x/old", url);
        Assert.Equal("FlickD-1.3.3-lite.zip", name);
    }

    [Fact]
    public void SelectAsset_ReturnsEmpty_WhenNoChannelMatch()
    {
        var root = Release("[{\"name\":\"FlickD-2.1.0-ocr.zip\",\"browser_download_url\":\"https://x/ocr\"}]");

        var (url, name, _, _) = UpdateService.SelectAsset(root, "full");

        Assert.Null(url);
        Assert.Null(name);
    }

    [Fact]
    public void SelectAsset_ReturnsEmpty_WhenAssetsMissingOrNotAnArray()
    {
        using var noAssets = JsonDocument.Parse("{\"tag_name\":\"v2.1.0\"}");
        Assert.Null(UpdateService.SelectAsset(noAssets.RootElement, "full").Url);

        using var badAssets = JsonDocument.Parse("{\"assets\":\"oops\"}");
        Assert.Null(UpdateService.SelectAsset(badAssets.RootElement, "lite").Url);
    }

    [Fact]
    public void ParseDigest_ReturnsNull_WhenAssetHasNoDigest()
    {
        using var doc = JsonDocument.Parse("{\"name\":\"FlickD-2.1.0-full.zip\"}");
        Assert.Null(UpdateService.ParseDigest(doc.RootElement));
    }

    [Fact]
    public void NormalizeSha256_AcceptsPrefixedBareAndMixedCase()
    {
        Assert.Equal(Hash, UpdateService.NormalizeSha256("sha256:" + LowerHash));
        Assert.Equal(Hash, UpdateService.NormalizeSha256("SHA256:" + Hash));
        Assert.Equal(Hash, UpdateService.NormalizeSha256(LowerHash));
        Assert.Equal(Hash, UpdateService.NormalizeSha256("  " + Hash + "  "));
    }

    [Fact]
    public void NormalizeSha256_RejectsMissingMalformedOrUnsupportedDigest()
    {
        Assert.Null(UpdateService.NormalizeSha256(null));
        Assert.Null(UpdateService.NormalizeSha256("  "));
        Assert.Null(UpdateService.NormalizeSha256("sha256:ABC"));
        Assert.Null(UpdateService.NormalizeSha256(new string('G', 64)));
        Assert.Null(UpdateService.NormalizeSha256("sha512:" + Hash));
        Assert.Null(UpdateService.NormalizeSha256(new string('A', 63)));
    }

    [Fact]
    public void VerifyDownloadedIntegrity_Passes_WhenSizeAndHashMatch()
    {
        UpdateService.VerifyDownloadedIntegrity("sha256:" + LowerHash, 42L, Hash, 42L);
    }

    [Fact]
    public void VerifyDownloadedIntegrity_Throws_OnHashMismatch()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            UpdateService.VerifyDownloadedIntegrity(Hash, 42L, OtherHash, 42L));
        Assert.Contains("SHA-256", ex.Message);
    }

    [Fact]
    public void VerifyDownloadedIntegrity_Throws_WhenDigestMissing()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            UpdateService.VerifyDownloadedIntegrity(null, 42L, Hash, 42L));
        Assert.Contains("手动下载", ex.Message);
    }

    [Fact]
    public void VerifyDownloadedIntegrity_Throws_OnSizeMismatch()
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdateService.VerifyDownloadedIntegrity(Hash, 42L, Hash, 41L));
    }

    [Fact]
    public void EnsureDownloadedSize_SkipsUnknownAndPassesEqual()
    {
        UpdateService.EnsureDownloadedSize(null, 999);
        UpdateService.EnsureDownloadedSize(-1, 999);
        UpdateService.EnsureDownloadedSize(10, 10);
    }

    [Fact]
    public void EnsureDownloadedSize_ThrowsWhenDifferent()
    {
        Assert.Throws<InvalidDataException>(() => UpdateService.EnsureDownloadedSize(10, 9));
    }

    [Fact]
    public void IsValidPayload_RequiresPrimaryExe_AndRejectsLegacyExe()
    {
        using var d = new TempDir();
        Assert.False(UpdateService.IsValidPayload(d.Root));

        File.WriteAllBytes(Path.Combine(d.Root, "LanFlow.exe"), new byte[] { 1, 2, 3 });
        Assert.False(UpdateService.IsValidPayload(d.Root));

        File.WriteAllBytes(Path.Combine(d.Root, "FlickD.exe"), new byte[] { 1, 2, 3 });
        Assert.True(UpdateService.IsValidPayload(d.Root));
    }

    [Fact]
    public void IsValidPayload_RejectsEmptyPrimaryExeAndMissingDirectory()
    {
        using var d = new TempDir();
        File.WriteAllBytes(Path.Combine(d.Root, "FlickD.exe"), Array.Empty<byte>());
        Assert.False(UpdateService.IsValidPayload(d.Root));
        Assert.False(UpdateService.IsValidPayload(Path.Combine(
            Path.GetTempPath(), "FlickDTests_Missing_" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void BuildUpdaterScript_BackupsWholeInstallAndRollsBackOnFailure()
    {
        var script = UpdateService.BuildUpdaterScript(@"C:\temp\payload", @"C:\app", 1234);

        Assert.Contains("set \"PID=1234\"", script);
        Assert.Contains("set \"SRC=C:\\temp\\payload\"", script);
        Assert.Contains("set \"DST=C:\\app\"", script);
        Assert.Contains("xcopy /s /y /q \"%DST%\\*\" \"%BACKUP%\"", script);
        Assert.Contains("xcopy /s /y /q \"%SRC%\\*\" \"%DST%\"", script);
        Assert.Contains("if errorlevel 1 goto rollback", script);
        Assert.Contains(":rollback", script);
        Assert.Contains("xcopy /s /y /q \"%BACKUP%\\*\" \"%DST%\"", script);
    }

    [Fact]
    public void BuildUpdaterScript_HasNoLegacyExeNameOrQuotedSetValues()
    {
        var script = UpdateService.BuildUpdaterScript(@"C:\temp\p", @"C:\app", 7);

        Assert.DoesNotContain("LanFlow.exe", script);
        Assert.DoesNotContain("set SRC=", script);
        Assert.DoesNotContain("set DST=", script);
        Assert.Contains("chcp 65001", script);
        Assert.Contains("\"%DST%\\FlickD.exe\"", script);
    }

    [Fact]
    public void BuildUpdaterScript_CleansWholeWorkDir_AndKeepsBackupWhenRollbackFails()
    {
        var script = UpdateService.BuildUpdaterScript(
            @"C:\temp\work\payload", @"C:\app", 1234, @"C:\temp\work");

        Assert.Contains("set \"WORK=C:\\temp\\work\"", script);
        Assert.Contains("rd /s /q \"%WORK%\"", script);
        Assert.Contains("if errorlevel 1 goto backupfailed", script);
        Assert.Contains(":backupfailed", script);
        Assert.Contains("if errorlevel 1 goto keepbackup", script);
        Assert.Contains(":keepbackup", script);
        Assert.Contains("FlickDUpdateFailure.txt", script);
        Assert.Contains("exit /b 1", script);
    }

    [Fact]
    public void BuildUpdaterScript_WithoutWorkDir_FallsBackToPayloadCleanup()
    {
        var script = UpdateService.BuildUpdaterScript(@"C:\temp\payload", @"C:\app", 7);

        Assert.Contains("set \"WORK=C:\\temp\\payload\"", script);
        Assert.Contains("rd /s /q \"%WORK%\"", script);
    }
}
