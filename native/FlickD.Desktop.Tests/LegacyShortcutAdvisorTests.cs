using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlickD.Desktop.Services;
using Xunit;

namespace FlickD.Desktop.Tests;

public sealed class LegacyShortcutAdvisorTests : IDisposable
{
    private readonly string _rootDirectory;
    private readonly string _shortcutDirectory;

    public LegacyShortcutAdvisorTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "flickd-shortcut-" + Guid.NewGuid().ToString("N"));
        _shortcutDirectory = Path.Combine(_rootDirectory, "shortcuts");
        Directory.CreateDirectory(_shortcutDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响测试结论。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Theory]
    [InlineData(@"C:\x\LanFlow.exe", true)]
    [InlineData("lanflow.exe", true)]
    [InlineData(@"C:\x\FlickD.exe", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsLegacyLauncher_MatchesLegacyFileNameOnly(string? targetPath, bool expected)
    {
        Assert.Equal(expected, LegacyShortcutAdvisor.IsLegacyLauncher(targetPath));
    }

    [Fact]
    public void FindLegacyShortcuts_ReturnsOnlyShortcutsPointingToLegacyLauncher()
    {
        var legacyPath = CreateShortcut("a.lnk");
        var currentPath = CreateShortcut("b.lnk");
        var otherPath = CreateShortcut("c.lnk");
        var resolver = new FakeTargetResolver(new()
        {
            [legacyPath] = @"C:\Apps\LanFlow\LanFlow.exe",
            [currentPath] = @"C:\Apps\FlickD\FlickD.exe",
            [otherPath] = @"C:\Windows\System32\notepad.exe",
        });

        var matches = LegacyShortcutAdvisor.FindLegacyShortcuts(resolver, [_shortcutDirectory]);

        var match = Assert.Single(matches);
        Assert.Equal(legacyPath, match, ignoreCase: true);
    }

    [Fact]
    public void TryCreateNotice_FirstCallReturnsNoticeAndCreatesMarker_SecondCallReturnsNull()
    {
        var legacyPath = CreateShortcut("a.lnk");
        var markerPath = Path.Combine(_rootDirectory, "state", "legacy-shortcut-notice.flag");
        var resolver = new FakeTargetResolver(new()
        {
            [legacyPath] = @"C:\Apps\LanFlow\LanFlow.exe",
        });

        var first = LegacyShortcutAdvisor.TryCreateNotice(resolver, [_shortcutDirectory], markerPath);
        Assert.NotNull(first);
        Assert.Contains("LanFlow.exe", first);
        Assert.True(File.Exists(markerPath));

        var second = LegacyShortcutAdvisor.TryCreateNotice(resolver, [_shortcutDirectory], markerPath);
        Assert.Null(second);
    }

    [Fact]
    public void TryCreateNotice_WithoutLegacyShortcuts_ReturnsNullAndSkipsMarker()
    {
        var currentPath = CreateShortcut("a.lnk");
        var markerPath = Path.Combine(_rootDirectory, "state", "legacy-shortcut-notice.flag");
        var resolver = new FakeTargetResolver(new()
        {
            [currentPath] = @"C:\Apps\FlickD\FlickD.exe",
        });

        var notice = LegacyShortcutAdvisor.TryCreateNotice(resolver, [_shortcutDirectory], markerPath);

        Assert.Null(notice);
        Assert.False(File.Exists(markerPath));
    }

    private string CreateShortcut(string fileName)
    {
        var path = Path.Combine(_shortcutDirectory, fileName);
        File.WriteAllBytes(path, []);
        return path;
    }

    private sealed class FakeTargetResolver : IShortcutTargetResolver
    {
        private readonly Dictionary<string, string> _targets;

        public FakeTargetResolver(Dictionary<string, string> targets) => _targets = targets;

        public string ResolveTargetPath(string path) =>
            _targets.TryGetValue(path, out var target) ? target : path;
    }
}
