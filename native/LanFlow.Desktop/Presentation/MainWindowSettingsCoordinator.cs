using System;
using System.Windows;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Presentation;

public sealed class MainWindowSettingsCoordinator
{
    private readonly ThemeResourceUpdater _themeResourceUpdater;
    private readonly WindowAppearanceController _windowAppearanceController;
    private readonly ResourceDictionary _resources;
    private readonly Window _window;
    private readonly FrameworkElement _surfaceRoot;
    private readonly FrameworkElement _contentRoot;
    private readonly Func<Settings, Settings> _applyWorkingSettings;
    private readonly Func<Settings, Settings> _persistSettings;
    private readonly Action<Settings> _applyLayoutParameters;
    private readonly Action<Settings> _applyNavigationParameters;
    private readonly Action<Settings> _applyAnimationMode;
    private readonly Action<Settings> _applyIconSize;

    public MainWindowSettingsCoordinator(
        ThemeResourceUpdater themeResourceUpdater,
        WindowAppearanceController windowAppearanceController,
        ResourceDictionary resources,
        Window window,
        FrameworkElement surfaceRoot,
        FrameworkElement contentRoot,
        Func<Settings, Settings> applyWorkingSettings,
        Func<Settings, Settings> persistSettings,
        Action<Settings> applyLayoutParameters,
        Action<Settings> applyNavigationParameters,
        Action<Settings> applyAnimationMode,
        Action<Settings> applyIconSize)
    {
        _themeResourceUpdater = themeResourceUpdater ?? throw new ArgumentNullException(nameof(themeResourceUpdater));
        _windowAppearanceController = windowAppearanceController ?? throw new ArgumentNullException(nameof(windowAppearanceController));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _surfaceRoot = surfaceRoot ?? throw new ArgumentNullException(nameof(surfaceRoot));
        _contentRoot = contentRoot ?? throw new ArgumentNullException(nameof(contentRoot));
        _applyWorkingSettings = applyWorkingSettings ?? throw new ArgumentNullException(nameof(applyWorkingSettings));
        _persistSettings = persistSettings ?? throw new ArgumentNullException(nameof(persistSettings));
        _applyLayoutParameters = applyLayoutParameters ?? throw new ArgumentNullException(nameof(applyLayoutParameters));
        _applyNavigationParameters = applyNavigationParameters ?? throw new ArgumentNullException(nameof(applyNavigationParameters));
        _applyAnimationMode = applyAnimationMode ?? throw new ArgumentNullException(nameof(applyAnimationMode));
        _applyIconSize = applyIconSize ?? throw new ArgumentNullException(nameof(applyIconSize));
    }

    public void Preview(Settings settings) => ApplyCore(_applyWorkingSettings(settings));

    public void Apply(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ApplyCore(_persistSettings(settings));
    }

    public void Restore(Settings settings) => ApplyCore(_applyWorkingSettings(settings));

    private void ApplyCore(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _themeResourceUpdater.Apply(_resources, settings.ThemeColors);
        _windowAppearanceController.Apply(_window, _surfaceRoot, _contentRoot, settings);
        _applyLayoutParameters(settings);
        _applyNavigationParameters(settings);
        _applyAnimationMode(settings);
        _applyIconSize(settings);
    }
}
