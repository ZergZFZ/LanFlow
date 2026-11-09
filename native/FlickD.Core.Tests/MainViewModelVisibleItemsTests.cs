using FlickD.Desktop.Models;
using FlickD.Desktop.Services;
using FlickD.Desktop.ViewModels;

namespace FlickD.Core.Tests;

public sealed class MainViewModelVisibleItemsTests
{
    [Fact]
    public void GroupSwitch_UpdatesContentsWithoutReplacingCollection()
    {
        var first = new Group { Name = "A", Items = [new LauncherItem { Name = "A1" }] };
        var second = new Group { Name = "B", Items = [new LauncherItem { Name = "B1" }, new LauncherItem { Name = "B2" }] };
        var viewModel = Create(first, second);
        var visible = viewModel.VisibleItems;

        viewModel.SelectedGroup = second;

        Assert.Same(visible, viewModel.VisibleItems);
        Assert.Equal(["B1", "B2"], viewModel.VisibleItems.Select(item => item.Name));
    }

    [Fact]
    public void Search_UpdatesContentsWithoutReplacingCollection()
    {
        var group = new Group { Items = [new LauncherItem { Name = "Alpha" }, new LauncherItem { Name = "Beta" }] };
        var viewModel = Create(group);
        var visible = viewModel.VisibleItems;

        viewModel.SearchText = "bet";

        Assert.Same(visible, viewModel.VisibleItems);
        Assert.Equal("Beta", Assert.Single(viewModel.VisibleItems).Name);
    }
    [Fact]
    public void Startup_RestoresLastNonEmptyGroup()
    {
        var empty = new Group { Name = "Empty", Items = [] };
        var target = new Group { Name = "Target", Items = [new LauncherItem { Name = "App" }] };
        var config = new AppConfig { Groups = [empty, target] };
        config.Settings.LastGroupId = target.Id;

        var viewModel = new MainViewModel(new MemoryConfigStore(config));

        Assert.Same(target, viewModel.SelectedGroup);
        Assert.Equal("App", Assert.Single(viewModel.VisibleItems).Name);
    }

    [Fact]
    public void Startup_FallsBackFromEmptyLastGroupToFirstNonEmptyGroup()
    {
        var empty = new Group { Name = "Empty", Items = [] };
        var target = new Group { Name = "Target", Items = [new LauncherItem { Name = "App" }] };
        var config = new AppConfig { Groups = [empty, target] };
        config.Settings.LastGroupId = empty.Id;

        var viewModel = new MainViewModel(new MemoryConfigStore(config));

        Assert.Same(target, viewModel.SelectedGroup);
        Assert.Single(viewModel.VisibleItems);
    }

    [Fact]
    public void SelectedGroup_NullWriteBack_DoesNotClearVisibleItems()
    {
        var group = new Group { Items = [new LauncherItem { Name = "App" }] };
        var viewModel = Create(group);

        viewModel.SelectedGroup = null;

        Assert.Same(group, viewModel.SelectedGroup);
        Assert.Single(viewModel.VisibleItems);
    }

    [Fact]
    public void FrequencySort_OrdersItemsByUseCountDescending()
    {
        var group = new Group
        {
            SortMode = "frequency",
            Items = [
                new LauncherItem { Name = "Low", UseCount = 1 },
                new LauncherItem { Name = "High", UseCount = 5 },
            ],
        };

        var viewModel = Create(group);

        Assert.Equal(["High", "Low"], viewModel.VisibleItems.Select(item => item.Name));
    }

    private static MainViewModel Create(params Group[] groups) =>
        new(new MemoryConfigStore(new AppConfig { Groups = new(groups) }));

    private sealed class MemoryConfigStore(AppConfig config) : IConfigStore
    {
        public AppConfig Load() => config;
        public void Save(AppConfig value) { }
    }
}