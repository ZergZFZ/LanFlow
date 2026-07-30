using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;
using LanFlow.Desktop.ViewModels;

namespace LanFlow.Core.Tests;

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

    private static MainViewModel Create(params Group[] groups) =>
        new(new MemoryConfigStore(new AppConfig { Groups = new(groups) }));

    private sealed class MemoryConfigStore(AppConfig config) : IConfigStore
    {
        public AppConfig Load() => config;
        public void Save(AppConfig value) { }
    }
}