using System;
using System.Collections.Generic;
using System.IO;
using LanFlow.Desktop.Services.Ocr;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class OcrManifestVerifierTests
{
    [Fact]
    public void Verify_ReturnsEmpty_WhenAllFilesMatch()
    {
        using var fixture = new BundleFixture();
        var problems = OcrManifestVerifier.Verify(fixture.Root, fixture.Manifest);

        Assert.Empty(problems);
    }

    [Fact]
    public void Verify_ReportsMissingFile()
    {
        using var fixture = new BundleFixture();
        fixture.Manifest.Files.Add(new OcrManifestFile
        {
            Path = "runtime/missing.dll",
            Size = 12,
            Sha256 = new string('0', 64),
        });

        var problems = OcrManifestVerifier.Verify(fixture.Root, fixture.Manifest);

        Assert.Contains(problems, p => p.Contains("缺少文件", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_ReportsHashMismatch_WhenContentTampered()
    {
        using var fixture = new BundleFixture();
        var path = Path.Combine(fixture.Root, "models", "ppocr_keys.txt");
        File.AppendAllText(path, "tampered");

        var problems = OcrManifestVerifier.Verify(fixture.Root, fixture.Manifest);

        Assert.Contains(problems, p => p.Contains("大小不符", StringComparison.Ordinal) ||
                                       p.Contains("哈希不符", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_ReportsProblem_WhenManifestMissing()
    {
        using var fixture = new BundleFixture();
        var problems = OcrManifestVerifier.Verify(fixture.Root, null);

        Assert.Single(problems);
    }

    private sealed class BundleFixture : IDisposable
    {
        public BundleFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "lanflow-ocr-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "runtime"));
            Directory.CreateDirectory(Path.Combine(Root, "models"));
            File.WriteAllText(Path.Combine(Root, "runtime", "LanFlow.Ocr.dll"), "plugin");
            File.WriteAllText(Path.Combine(Root, "models", "ppocr_keys.txt"), "keys");

            var files = new List<OcrManifestFile>();
            foreach (var path in new[] { "runtime/LanFlow.Ocr.dll", "models/ppocr_keys.txt" })
            {
                var full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
                files.Add(new OcrManifestFile
                {
                    Path = path,
                    Size = new FileInfo(full).Length,
                    Sha256 = OcrManifestVerifier.ComputeSha256(full),
                });
            }

            Manifest = new OcrManifest
            {
                BundleId = "lanflow-ocr-win-x64",
                Version = "1.0.0",
                MinAppVersion = "1.6.0",
                Files = files,
            };
        }

        public string Root { get; }

        public OcrManifest Manifest { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // 测试清理失败不影响断言结果。
            }
        }
    }
}
