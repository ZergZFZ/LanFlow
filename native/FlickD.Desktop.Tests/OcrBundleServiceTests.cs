using System;
using System.IO;
using FlickD.Desktop.Services.Ocr;
using Xunit;

namespace FlickD.Desktop.Tests;

public sealed class OcrBundleServiceTests : IDisposable
{
    private readonly string _root;

    public OcrBundleServiceTests()
    {
        _root = Directory.CreateTempSubdirectory("lanflow-ocr-sweep-").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void SweepStaleBackups_RemovesOnlyOldBackupDirectories()
    {
        var target = Path.Combine(_root, "ocr");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(target + ".old-a");
        Directory.CreateDirectory(target + ".old-b");
        File.WriteAllText(Path.Combine(target + ".old-a", "x.dll"), "stale");
        // 同名前缀但非备份模式：不应被误删。
        var keep = target + "-data";
        Directory.CreateDirectory(keep);

        OcrBundleService.SweepStaleBackups(target);

        Assert.False(Directory.Exists(target + ".old-a"));
        Assert.False(Directory.Exists(target + ".old-b"));
        Assert.True(Directory.Exists(target));
        Assert.True(Directory.Exists(keep));
    }

    [Fact]
    public void SweepStaleBackups_MissingTarget_DoesNotThrow()
    {
        var target = Path.Combine(_root, "not-exists");
        var exception = Record.Exception(() => OcrBundleService.SweepStaleBackups(target));
        Assert.Null(exception);
    }
}
