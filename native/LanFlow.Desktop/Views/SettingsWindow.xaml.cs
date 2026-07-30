using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Presentation;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;
    private readonly Action? _clearIconCache;
    private readonly UpdateService _updateService = new();
    private bool _isLoading = true;

    public SettingsWindow(SettingsPreviewSession session, Action? clearIconCache = null)
    {
        _viewModel = new SettingsWindowViewModel(session ?? throw new ArgumentNullException(nameof(session)));
        _clearIconCache = clearIconCache;

        InitializeComponent();
        DataContext = _viewModel;
        LoadControls();
        ShowCategory(_viewModel.SelectedCategory.Id);
        _isLoading = false;
    }

    private Settings Working => _viewModel.Working;

    private void LoadControls()
    {
        ThemePresetCombo.SelectedIndex = Working.Theme == "light" ? 1 : Working.Theme == "custom" ? 2 : 0;
        ThemeProfileBox.Text = Working.ThemeProfile;

        GridLayoutRadio.IsChecked = Working.LayoutMode == SettingsOptionValues.GridLayout;
        ListLayoutRadio.IsChecked = Working.LayoutMode == SettingsOptionValues.ListLayout;
        CardLayoutRadio.IsChecked = Working.LayoutMode == SettingsOptionValues.CardLayout;
        if (GridLayoutRadio.IsChecked != true && ListLayoutRadio.IsChecked != true && CardLayoutRadio.IsChecked != true)
        {
            GridLayoutRadio.IsChecked = true;
        }

        IconSizeSlider.Value = Working.IconSize;
        CardWidthSlider.Value = Working.CardWidth;
        CardHeightSlider.Value = Working.CardHeight;
        TextSizeSlider.Value = Working.TextSize;
        ItemSpacingSlider.Value = Working.ItemSpacing;
        RowSpacingSlider.Value = Working.RowSpacing;
        ContentPaddingSlider.Value = Working.ContentPadding;
        ShowTitleCheck.IsChecked = Working.ShowItemTitle;
        ShowFullNameCheck.IsChecked = Working.ShowFullItemName;
        ShowShortcutBadgeCheck.IsChecked = Working.ShowShortcutBadge;

        GroupTopRadio.IsChecked = Working.GroupLayout == SettingsOptionValues.GroupTop;
        GroupLeftRadio.IsChecked = Working.GroupLayout != SettingsOptionValues.GroupTop;
        GroupClickRadio.IsChecked = Working.GroupSwitchMode != SettingsOptionValues.GroupSwitchHover;
        GroupHoverRadio.IsChecked = Working.GroupSwitchMode == SettingsOptionValues.GroupSwitchHover;
        GroupLabelSizeSlider.Value = Working.GroupLabelSize;
        GroupLabelFontSizeSlider.Value = Working.GroupLabelFontSize;
        GroupNavigationWidthSlider.Value = Working.GroupNavigationWidth;

        LayeredTransparencyRadio.IsChecked = Working.TransparencyMode != SettingsOptionValues.TransparencyWholeWindow;
        WholeWindowTransparencyRadio.IsChecked = Working.TransparencyMode == SettingsOptionValues.TransparencyWholeWindow;
        OpacitySlider.Value = _viewModel.CurrentOpacity;

        SingleClickOpenRadio.IsChecked = Working.OpenItemsOnSingleClick;
        DoubleClickOpenRadio.IsChecked = !Working.OpenItemsOnSingleClick;
        SystemAnimationRadio.IsChecked = Working.AnimationMode == SettingsOptionValues.AnimationSystem;
        OnAnimationRadio.IsChecked = Working.AnimationMode == SettingsOptionValues.AnimationOn;
        OffAnimationRadio.IsChecked = Working.AnimationMode == SettingsOptionValues.AnimationOff;
        if (SystemAnimationRadio.IsChecked != true && OnAnimationRadio.IsChecked != true && OffAnimationRadio.IsChecked != true)
        {
            SystemAnimationRadio.IsChecked = true;
        }

        HotkeyBox.Text = Working.Hotkey;
        RunAtStartupCheck.IsChecked = Working.StartWithWindows;
        LoadColorControls();
        RefreshValueLabels();
        VersionText.Text = "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.5");
    }

    private void LoadColorControls()
    {
        var colors = Working.ThemeColors;
        PanelColorBox.Text = colors.Panel;
        PanelBorderColorBox.Text = colors.PanelBorder;
        SurfaceColorBox.Text = colors.Surface;
        SurfaceBorderColorBox.Text = colors.SurfaceBorder;
        FooterColorBox.Text = colors.Footer;
        PrimaryTextColorBox.Text = colors.TextPrimary;
        SecondaryTextColorBox.Text = colors.TextSecondary;
        AccentColorBox.Text = colors.Accent;
        HoverColorBox.Text = colors.Hover;
        IconSurfaceColorBox.Text = colors.IconSurface;
        RefreshColorPickerButtons();
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is not SettingsCategory category) return;
        _viewModel.SelectedCategory = category;
        ShowCategory(category.Id);
    }

    private void ShowCategory(string categoryId)
    {
        if (AppearancePanel is null) return;

        var panels = new[]
        {
            AppearancePanel,
            LayoutPanel,
            GroupsPanel,
            TransparencyPanel,
            InteractionPanel,
            StartupPanel,
            PerformancePanel,
            AboutPanel,
        };
        foreach (var panel in panels) panel.Visibility = Visibility.Collapsed;

        var selectedPanel = categoryId switch
        {
            "layout" => LayoutPanel,
            "groups" => GroupsPanel,
            "transparency" => TransparencyPanel,
            "interaction" => InteractionPanel,
            "startup" => StartupPanel,
            "performance" => PerformancePanel,
            "about" => AboutPanel,
            _ => AppearancePanel,
        };
        selectedPanel.Visibility = Visibility.Visible;
        CategoryContentScrollViewer?.ScrollToTop();
    }

    private void ThemePresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ThemePresetCombo.SelectedItem is not ComboBoxItem { Tag: string theme }) return;

        _viewModel.Update(settings =>
        {
            settings.Theme = theme;
            settings.ThemeProfile = theme == "light" ? "浅色" : theme == "dark" ? "深色" : "自定义";
            settings.ThemeColors = theme == "light"
                ? ThemeColors.Light()
                : theme == "dark"
                    ? ThemeColors.Dark()
                    : settings.ThemeColors.Clone();
        });

        RunWhileLoading(() =>
        {
            ThemeProfileBox.Text = Working.ThemeProfile;
            LoadColorControls();
        });
    }

    private void ThemeProfileBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || sender is not TextBox textBox) return;
        _viewModel.Update(settings => settings.ThemeProfile = textBox.Text);
    }

    private void ThemeColorChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || sender is not TextBox textBox) return;

        _viewModel.Update(settings =>
        {
            SetThemeColor(settings.ThemeColors, textBox.Name, textBox.Text);
            if (!string.Equals(settings.Theme, "custom", StringComparison.Ordinal))
            {
                settings.ThemeProfile = "自定义";
            }
            settings.Theme = "custom";
        });

        RunWhileLoading(() =>
        {
            ThemePresetCombo.SelectedIndex = 2;
            ThemeProfileBox.Text = Working.ThemeProfile;
        });
        RefreshColorPickerButtons();
    }

    private static void SetThemeColor(ThemeColors colors, string controlName, string value)
    {
        switch (controlName)
        {
            case nameof(PanelColorBox): colors.Panel = value; break;
            case nameof(PanelBorderColorBox): colors.PanelBorder = value; break;
            case nameof(SurfaceColorBox): colors.Surface = value; break;
            case nameof(SurfaceBorderColorBox): colors.SurfaceBorder = value; break;
            case nameof(FooterColorBox): colors.Footer = value; break;
            case nameof(PrimaryTextColorBox): colors.TextPrimary = value; break;
            case nameof(SecondaryTextColorBox): colors.TextSecondary = value; break;
            case nameof(AccentColorBox): colors.Accent = value; break;
            case nameof(HoverColorBox): colors.Hover = value; break;
            case nameof(IconSurfaceColorBox): colors.IconSurface = value; break;
        }
    }

    private void ColorPicker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } || FindName(name) is not TextBox textBox) return;
        var picker = new ColorPickerWindow(textBox.Text) { Owner = this };
        if (picker.ShowDialog() == true) textBox.Text = picker.SelectedColor;
    }

    private void RefreshColorPickerButtons()
    {
        foreach (var button in FindVisualChildren<Button>(this).Where(button => button.Tag is string))
        {
            var name = (string)button.Tag;
            if (FindName(name) is not TextBox textBox) continue;
            if (FindName(name + "Swatch") is not Border swatch) continue;
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(textBox.Text);
                swatch.Background = new System.Windows.Media.SolidColorBrush(color);
            }
            catch (FormatException)
            {
                swatch.ClearValue(Border.BackgroundProperty);
            }
        }
    }

    private void LayoutMode_Changed(object sender, RoutedEventArgs e) =>
        UpdateTaggedOption(sender, value => _viewModel.Update(settings => settings.LayoutMode = value));

    private void GroupLayout_Changed(object sender, RoutedEventArgs e) =>
        UpdateTaggedOption(sender, value => _viewModel.Update(settings => settings.GroupLayout = value));

    private void GroupSwitchMode_Changed(object sender, RoutedEventArgs e) =>
        UpdateTaggedOption(sender, value => _viewModel.Update(settings => settings.GroupSwitchMode = value));

    private void AnimationMode_Changed(object sender, RoutedEventArgs e) =>
        UpdateTaggedOption(sender, value => _viewModel.Update(settings => settings.AnimationMode = value));

    private void OpenMode_Changed(object sender, RoutedEventArgs e)
    {
        UpdateTaggedOption(sender, value =>
            _viewModel.Update(settings => settings.OpenItemsOnSingleClick = value == "single"));
    }

    private void TransparencyMode_Changed(object sender, RoutedEventArgs e)
    {
        UpdateTaggedOption(sender, value =>
        {
            _viewModel.Update(settings =>
            {
                settings.TransparencyMode = value;
                settings.Opacity = value == SettingsOptionValues.TransparencyWholeWindow
                    ? settings.WholeWindowOpacity
                    : settings.LayeredOpacity;
            });
            RefreshOpacityControls();
        });
    }

    private void UpdateTaggedOption(object sender, Action<string> update)
    {
        if (_isLoading || sender is not RadioButton { IsChecked: true, Tag: string value }) return;
        update(value);
        RefreshValueLabels();
    }

    private void NumericSettingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading || sender is not Slider { Tag: string settingKey } slider) return;
        var value = slider.Value;

        _viewModel.Update(settings =>
        {
            switch (settingKey)
            {
                case "iconSize": settings.IconSize = value; break;
                case "cardWidth": settings.CardWidth = value; break;
                case "cardHeight": settings.CardHeight = value; break;
                case "textSize": settings.TextSize = value; break;
                case "itemSpacing": settings.ItemSpacing = value; break;
                case "rowSpacing": settings.RowSpacing = value; break;
                case "contentPadding": settings.ContentPadding = value; break;
                case "groupLabelSize": settings.GroupLabelSize = value; break;
                case "groupLabelFontSize": settings.GroupLabelFontSize = value; break;
                case "groupNavigationWidth": settings.GroupNavigationWidth = value; break;
            }
        });
        RefreshValueLabels();
    }

    private void BooleanSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox { Tag: string settingKey } checkBox) return;
        var value = checkBox.IsChecked == true;

        _viewModel.Update(settings =>
        {
            switch (settingKey)
            {
                case "showItemTitle": settings.ShowItemTitle = value; break;
                case "showFullItemName": settings.ShowFullItemName = value; break;
                case "showShortcutBadge": settings.ShowShortcutBadge = value; break;
                case "startWithWindows": settings.StartWithWindows = value; break;
            }
        });
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        UpdateCurrentOpacity(OpacitySlider.Value);
        RefreshValueLabels();
    }

    private void UpdateCurrentOpacity(double opacity)
    {
        var normalized = Math.Clamp(opacity, 0.55, 1.0);
        _viewModel.Update(settings =>
        {
            if (settings.TransparencyMode == SettingsOptionValues.TransparencyWholeWindow)
            {
                settings.WholeWindowOpacity = normalized;
            }
            else
            {
                settings.LayeredOpacity = normalized;
            }
            settings.Opacity = normalized;
        });
    }

    private void OpacityPercentBox_LostFocus(object sender, RoutedEventArgs e) => ApplyOpacityPercentText();

    private void OpacityPercentBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        ApplyOpacityPercentText();
        Keyboard.ClearFocus();
    }

    private void ApplyOpacityPercentText()
    {
        var text = OpacityPercentBox.Text.Trim().TrimEnd('%').Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var percent) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
        {
            RefreshOpacityControls();
            return;
        }

        UpdateCurrentOpacity(Math.Clamp(percent, 55, 100) / 100.0);
        RefreshOpacityControls();
    }

    private void ResetOpacity_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetCurrentOpacity();
        RefreshOpacityControls();
    }

    private void RefreshOpacityControls()
    {
        RunWhileLoading(() =>
        {
            OpacitySlider.Value = _viewModel.CurrentOpacity;
            OpacityPercentBox.Text = (_viewModel.CurrentOpacity * 100).ToString("0", CultureInfo.CurrentCulture);
        });
    }

    private void RefreshValueLabels()
    {
        IconSizeValue.Text = $"{Working.IconSize:0} DIP";
        CardWidthValue.Text = $"{Working.CardWidth:0} DIP";
        CardHeightValue.Text = $"{Working.CardHeight:0} DIP";
        TextSizeValue.Text = $"{Working.TextSize:0} pt";
        ItemSpacingValue.Text = $"{Working.ItemSpacing:0} DIP";
        RowSpacingValue.Text = $"{Working.RowSpacing:0} DIP";
        ContentPaddingValue.Text = $"{Working.ContentPadding:0} DIP";
        GroupLabelSizeValue.Text = $"{Working.GroupLabelSize:0} DIP";
        GroupLabelFontSizeValue.Text = $"{Working.GroupLabelFontSize:0} pt";
        GroupNavigationWidthValue.Text = $"{Working.GroupNavigationWidth:0} DIP";
        OpacityPercentBox.Text = (_viewModel.CurrentOpacity * 100).ToString("0", CultureInfo.CurrentCulture);
    }

    private void HotkeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

        var modifiers = Keyboard.Modifiers;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        var candidate = string.Join('+', parts.Append(key.ToString()));
        if (!HotkeyService.TryNormalize(candidate, out var normalized)) return;

        _viewModel.Update(settings => settings.Hotkey = normalized);
        HotkeyBox.Text = normalized;
    }

    private void ClearIconCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _clearIconCache?.Invoke();
            CacheStatusText.Text = _clearIconCache is null ? "当前未连接图标缓存服务。" : "图标缓存已清空，将在需要时重新加载。";
        }
        catch (Exception ex)
        {
            CacheStatusText.Text = "清空缓存失败：" + ex.Message;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.Apply();
        DialogResult = true;
    }

    private void AboutLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 忽略无法打开浏览器的情况。
        }
        e.Handled = true;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新…";
        try
        {
            var info = await _updateService.CheckAsync();
            if (!info.HasUpdate)
            {
                CheckUpdateButton.IsEnabled = true;
                UpdateStatusText.Text = string.IsNullOrEmpty(info.LatestVersion)
                    ? "暂时无法获取更新信息，请稍后重试。"
                    : $"已是最新版本 v{info.CurrentVersion}";
                return;
            }

            if (info.DownloadUrl is null || info.AssetName is null)
            {
                CheckUpdateButton.IsEnabled = true;
                UpdateStatusText.Text = $"发现新版本 v{info.LatestVersion}，但未找到匹配（{UpdateService.CurrentChannel}）的下载文件，请前往开源地址手动下载。";
                return;
            }

            UpdateStatusText.Text = $"发现新版本 v{info.LatestVersion}，正在下载并更新…";
            var progress = new Progress<double>(value => UpdateStatusText.Text = $"正在下载更新… {value:P0}");
            await _updateService.DownloadAndApplyAsync(info.DownloadUrl, info.AssetName, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            CheckUpdateButton.IsEnabled = true;
            UpdateStatusText.Text = "更新失败：" + ex.Message + "（可前往开源地址手动下载）";
        }
    }

    private void RunWhileLoading(Action action)
    {
        var previous = _isLoading;
        _isLoading = true;
        try
        {
            action();
        }
        finally
        {
            _isLoading = previous;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
