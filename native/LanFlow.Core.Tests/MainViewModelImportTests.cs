using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;
using LanFlow.Desktop.ViewModels;

namespace LanFlow.Core.Tests;

public sealed class MainViewModelImportTests
{
    [Fact]
    public void SaveAndApply_WhenSaveFails_KeepsCurrentInMemoryConfig()
    {
        var original = ConfigWithGroup("original", "原分组");
        var merged = ConfigWithGroup("merged", "新分组");
        var store = new RecordingConfigStore(original) { SaveException = new IOException("磁盘只读") };
        var viewModel = new MainViewModel(store);

        var exception = Assert.Throws<IOException>(() => viewModel.SaveAndApply(merged));

        Assert.Equal("磁盘只读", exception.Message);
        Assert.Same(original, viewModel.Config);
        Assert.Equal("original", viewModel.SelectedGroup?.Id);
        Assert.Equal(1, store.SaveCallCount);
        Assert.Same(merged, store.LastSavedConfig);
    }

    [Fact]
    public void SaveAndApply_WhenSaveSucceeds_ReplacesConfigAfterSingleSaveAndPreservesSelectionById()
    {
        var original = new AppConfig
        {
            Groups =
            [
                new Group { Id = "first", Name = "一" },
                new Group { Id = "selected", Name = "二" },
            ],
        };
        var merged = new AppConfig
        {
            Groups =
            [
                new Group { Id = "first", Name = "一" },
                new Group { Id = "selected", Name = "二" },
                new Group { Id = "new", Name = "三" },
            ],
        };
        var store = new RecordingConfigStore(original);
        var viewModel = new MainViewModel(store) { SelectedGroup = original.Groups[1] };

        viewModel.SaveAndApply(merged);

        Assert.Equal(1, store.SaveCallCount);
        Assert.Same(merged, viewModel.Config);
        Assert.Equal("selected", viewModel.SelectedGroup?.Id);
        Assert.Equal("就绪", viewModel.StatusText);
    }


    [Fact]
    public void SaveAndApply_AfterSave_DoesNotRaiseUiNotificationsInsideCommitBoundary()
    {
        var original = ConfigWithGroup("original", "原分组");
        var merged = ConfigWithGroup("merged", "新分组");
        var store = new RecordingConfigStore(original);
        var viewModel = new MainViewModel(store);
        viewModel.PropertyChanged += (_, _) => throw new InvalidOperationException("界面通知失败");

        var exception = Record.Exception(() => viewModel.SaveAndApply(merged));

        Assert.Null(exception);
        Assert.Equal(1, store.SaveCallCount);
        Assert.Same(merged, viewModel.Config);
        Assert.Equal("merged", viewModel.SelectedGroup?.Id);
    }
    private static AppConfig ConfigWithGroup(string id, string name) => new()
    {
        Groups = [new Group { Id = id, Name = name }],
    };

    private sealed class RecordingConfigStore(AppConfig loadedConfig) : IConfigStore
    {
        public int SaveCallCount { get; private set; }
        public AppConfig? LastSavedConfig { get; private set; }
        public Exception? SaveException { get; init; }

        public AppConfig Load() => loadedConfig;

        public void Save(AppConfig config)
        {
            SaveCallCount++;
            LastSavedConfig = config;
            if (SaveException is not null) throw SaveException;
        }
    }
}