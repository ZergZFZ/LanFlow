using System;
using System.IO;
using LanFlow.Desktop.Diagnostics;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class LogWriterTests : IDisposable
{
    private readonly string _directory;

    public LogWriterTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "lanflow-logwriter-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Append_CreatesFile_AndWritesText()
    {
        var path = Path.Combine(_directory, "log.txt");

        LogWriter.Append(path, "first" + Environment.NewLine);

        Assert.Equal("first" + Environment.NewLine, File.ReadAllText(path));
    }

    [Fact]
    public void Append_RotatesToBackup_WhenSizeExceedsLimit()
    {
        var path = Path.Combine(_directory, "log.txt");
        var first = new string('a', 30) + Environment.NewLine;
        var second = new string('b', 30) + Environment.NewLine;

        LogWriter.Append(path, first, maxBytes: 30);
        LogWriter.Append(path, second, maxBytes: 30);

        Assert.Equal(second, File.ReadAllText(path));
        Assert.Equal(first, File.ReadAllText(path + ".1"));
    }

    [Fact]
    public void Append_NeverThrows_EvenForInvalidPath()
    {
        var invalidPath = Path.Combine(_directory, "inva|id.log");

        LogWriter.Append(invalidPath, "ignored");

        Assert.False(File.Exists(invalidPath));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响测试结果。
        }
    }
}
