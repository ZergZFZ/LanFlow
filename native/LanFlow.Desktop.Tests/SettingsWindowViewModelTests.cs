using System.Collections.Generic;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Presentation;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class SettingsWindowViewModelTests
{
    private static readonly string[] ExpectedCategories =
    [
        "外观与主题",
        "布局与项目",
        "分组标签",
        "透明度与材质",
        "交互与动画",
        "启动与快捷键",
        "性能与缓存",
        "关于",
    ];

    private static readonly string[] ExpectedSettingKeys =
    [
        "hotkey",
        "theme",
        "themeProfile",
        "themeColors",
        "customThemes",
        "opacity",
        "layoutMode",
        "iconSize",
        "cardWidth",
        "cardHeight",
        "cardSize",
        "textSize",
        "itemSpacing",
        "rowSpacing",
        "contentPadding",
        "showShortcutBadge",
        "showFullItemName",
        "showItemTitle",
        "groupLayout",
        "groupSwitchMode",
        "groupLabelSize",
        "groupLabelFontSize",
        "groupNavigationWidth",
        "transparencyMode",
        "layeredOpacity",
        "wholeWindowOpacity",
        "animationMode",
        "startWithWindows",
        "openItemsOnSingleClick",
    ];

    [Fact]
    public void Categories_CoverApprovedInformationArchitecture()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(ExpectedCategories, viewModel.Categories.Select(category => category.Title));
        Assert.Equal("appearance", viewModel.SelectedCategory.Id);
    }

    [Fact]
    public void Categories_MapEveryPersistedSettingsFieldExactlyOnce()
    {
        var viewModel = CreateViewModel();
        var covered = viewModel.Categories.SelectMany(category => category.SettingKeys).ToArray();

        Assert.Equal(ExpectedSettingKeys.Order(), covered.Order());
        Assert.Equal(covered.Length, covered.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(SettingsOptionValues.GroupLeft, true)]
    [InlineData(SettingsOptionValues.GroupTop, false)]
    public void LeftNavigationWidth_OnlyEnabledForLeftLayout(string layout, bool expected)
    {
        var viewModel = CreateViewModel();

        viewModel.Update(settings => settings.GroupLayout = layout);

        Assert.Equal(expected, viewModel.IsLeftNavigationWidthEnabled);
    }

    [Theory]
    [InlineData(SettingsOptionValues.TransparencyLayered, 0.61, 0.72)]
    [InlineData(SettingsOptionValues.TransparencyWholeWindow, 0.61, 0.72)]
    public void ResetCurrentOpacity_OnlyResetsActiveMode(string mode, double layered, double wholeWindow)
    {
        var viewModel = CreateViewModel(new Settings
        {
            TransparencyMode = mode,
            LayeredOpacity = layered,
            WholeWindowOpacity = wholeWindow,
            Opacity = mode == SettingsOptionValues.TransparencyWholeWindow ? wholeWindow : layered,
        });

        viewModel.ResetCurrentOpacity();

        Assert.Equal(0.85, viewModel.CurrentOpacity, 3);
        Assert.Equal(mode == SettingsOptionValues.TransparencyLayered ? 0.85 : layered, viewModel.Working.LayeredOpacity, 3);
        Assert.Equal(mode == SettingsOptionValues.TransparencyWholeWindow ? 0.85 : wholeWindow, viewModel.Working.WholeWindowOpacity, 3);
        Assert.Equal(0.85, viewModel.Working.Opacity, 3);
    }

    [Fact]
    public void Update_RaisesDerivedStateNotificationsAndCancelRestoresBaseline()
    {
        var viewModel = CreateViewModel(new Settings
        {
            GroupLayout = SettingsOptionValues.GroupLeft,
            TransparencyMode = SettingsOptionValues.TransparencyLayered,
            LayeredOpacity = 0.85,
            Opacity = 0.85,
        });
        var changed = new HashSet<string>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);

        viewModel.Update(settings =>
        {
            settings.GroupLayout = SettingsOptionValues.GroupTop;
            settings.LayeredOpacity = 0.6;
            settings.Opacity = 0.6;
        });

        Assert.True(viewModel.HasChanges);
        Assert.False(viewModel.IsLeftNavigationWidthEnabled);
        Assert.Equal(0.6, viewModel.CurrentOpacity, 3);
        Assert.Contains(nameof(SettingsWindowViewModel.Working), changed);
        Assert.Contains(nameof(SettingsWindowViewModel.HasChanges), changed);
        Assert.Contains(nameof(SettingsWindowViewModel.IsLeftNavigationWidthEnabled), changed);
        Assert.Contains(nameof(SettingsWindowViewModel.CurrentOpacity), changed);

        viewModel.Cancel();

        Assert.False(viewModel.HasChanges);
        Assert.True(viewModel.IsLeftNavigationWidthEnabled);
        Assert.Equal(0.85, viewModel.CurrentOpacity, 3);
    }

    private static SettingsWindowViewModel CreateViewModel(Settings? settings = null) =>
        new(new SettingsPreviewSession(settings ?? new Settings()));
}
