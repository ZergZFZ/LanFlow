using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Core.Tests;

public sealed class WorkspaceSearchTests
{
    private static LauncherItem Item(string name, string path, string command = "", long useCount = 0) =>
        new() { Name = name, Path = path, Command = command, UseCount = useCount };

    private static AppConfig CreateConfig() => new()
    {
        Groups =
        [
            new Group
            {
                Id = "work",
                Name = "工作",
                Items =
                [
                    Item("微信", @"C:\Apps\WeChat\wechat.exe"),
                    Item("记事本", @"C:\Windows\notepad.exe", useCount: 2),
                ],
            },
            new Group
            {
                Id = "fun",
                Name = "娱乐",
                Items =
                [
                    Item("网易云音乐", @"D:\Music\netease.exe", useCount: 9),
                    Item("企业微信", @"C:\Apps\wework.exe"),
                ],
            },
        ],
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyQuery_ReturnsNoMatches(string? query)
    {
        Assert.Empty(WorkspaceSearch.Search(CreateConfig(), query));
    }

    [Fact]
    public void NamePrefixMatch_CarriesItsGroup()
    {
        var matches = WorkspaceSearch.Search(CreateConfig(), "微信");

        Assert.Equal(2, matches.Count);
        Assert.Equal("微信", matches[0].Item.Name);
        Assert.Equal("work", matches[0].Group.Id);
        Assert.Equal("企业微信", matches[1].Item.Name);
        Assert.Equal("fun", matches[1].Group.Id);
    }

    [Fact]
    public void PrefixBeatsContains_RegardlessOfUseCount()
    {
        // 企业微信 useCount=0 但名称前缀命中（5 分）应排在名称包含（3 分）之前——
        // 等等，"微信" 是「微信」的前缀（5 分），也是「企业微信」的包含（3 分）。
        var matches = WorkspaceSearch.Search(CreateConfig(), "微信");

        Assert.True(matches[0].Score > matches[1].Score);
        Assert.Equal(5, matches[0].Score);
        Assert.Equal(3, matches[1].Score);
    }

    [Fact]
    public void PinyinInitialPrefix_MatchesChineseItems()
    {
        // 微信 → WX，查询 "wx" 应以拼音首字母前缀命中（4 分）
        var matches = WorkspaceSearch.Search(CreateConfig(), "wx");

        var wechat = Assert.Single(matches, m => m.Item.Name == "微信");
        Assert.Equal(4, wechat.Score);
    }

    [Fact]
    public void PinyinInitialContain_MatchesWithLowerScore()
    {
        // 网易云音乐 → WLYY；查询 "yy" 是首字母串的包含（2 分）
        var matches = WorkspaceSearch.Search(CreateConfig(), "yy");

        var item = Assert.Single(matches, m => m.Item.Name == "网易云音乐");
        Assert.Equal(2, item.Score);
    }

    [Fact]
    public void PathMatch_HasLowestScore()
    {
        var matches = WorkspaceSearch.Search(CreateConfig(), "netease");

        var item = Assert.Single(matches);
        Assert.Equal("网易云音乐", item.Item.Name);
        Assert.Equal(1, item.Score);
    }

    [Fact]
    public void SameScore_ItemsOrderByUseCountDescending()
    {
        // 「记事本」与另一同名项同分时，使用频次高者在前
        var config = new AppConfig
        {
            Groups =
            [
                new Group { Id = "a", Name = "A", Items = [Item("便签", @"C:\a.exe", useCount: 1)] },
                new Group { Id = "b", Name = "B", Items = [Item("便签", @"C:\b.exe", useCount: 7)] },
            ],
        };

        var matches = WorkspaceSearch.Search(config, "便签");

        Assert.Equal(2, matches.Count);
        Assert.Equal(@"C:\b.exe", matches[0].Item.Path);
        Assert.Equal(7, matches[0].Item.UseCount);
    }

    [Fact]
    public void ChineseQuery_DoesNotTriggerPinyinPath()
    {
        // 中文查询只做字面匹配：不应把中文查询转成拼音再匹配英文名
        var config = CreateConfig();

        Assert.Empty(WorkspaceSearch.Search(config, "维信"));
        Assert.Empty(WorkspaceSearch.Search(config, "网抑云"));
    }

    [Fact]
    public void NoMatch_ReturnsEmpty()
    {
        Assert.Empty(WorkspaceSearch.Search(CreateConfig(), "photoshop"));
    }
}
