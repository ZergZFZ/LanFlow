using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using Microsoft.Win32;
using FlickD.Desktop.Models;
using FlickD.Desktop.Presentation;
using FlickD.Desktop.Services;
using FlickD.Desktop.Services.Ocr;

namespace FlickD.Desktop.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;
    private readonly Action? _clearIconCache;
    private readonly SettingsMaintenanceService? _maintenanceService;
    private static readonly TimeSpan PreviewInterval = TimeSpan.FromMilliseconds(33);
    private readonly UpdateService _updateService = new();
    private readonly Dictionary<string, PreviewThrottle<double>> _previewThrottles = [];
    private bool _isLoading = true;

    public UnsavedCloseDecision CloseDecision { get; private set; } = UnsavedCloseDecision.KeepEditing;

    public SettingsWindow(SettingsPreviewSession session, Action? clearIconCache = null, SettingsMaintenanceService? maintenanceService = null)
    {
        _viewModel = new SettingsWindowViewModel(session ?? throw new ArgumentNullException(nameof(session)));
        _clearIconCache = clearIconCache;
        _maintenanceService = maintenanceService;

        InitializeComponent();
        DataContext = _viewModel;
        InitializePreviewThrottles();
        LoadControls();
        ShowCategory(_viewModel.SelectedCategory.Id);
        _isLoading = false;
    }

    private Settings Working => _viewModel.Working;

    public void FlushPendingPreviews() => FlushPreviewThrottles();

    public void DisposePreviewThrottles()
    {
        foreach (var throttle in _previewThrottles.Values)
        {
            throttle.Dispose();
        }
        _previewThrottles.Clear();
    }

    private void InitializePreviewThrottles()
    {
        var scheduler = new DispatcherTimerScheduler(Dispatcher);
        foreach (var settingKey in new[]
                 {
                     "iconSize",
                     "cardWidth",
                     "cardHeight",
                     "textSize",
                     "itemSpacing",
                     "rowSpacing",
                     "contentPadding",
                     "groupLabelSize",
                     "groupLabelFontSize",
                     "groupNavigationWidth",
                     "groupHoverDelayMs",
                 })
        {
            _previewThrottles.Add(
                settingKey,
                new PreviewThrottle<double>(
                    PreviewInterval,
                    scheduler,
                    value => _viewModel.UpdateContinuousSetting(settingKey, value)));
        }

        _previewThrottles.Add(
            "opacity",
            new PreviewThrottle<double>(
                PreviewInterval,
                scheduler,
                _viewModel.UpdateCurrentOpacity));
    }

    private void FlushPreviewThrottles()
    {
        foreach (var throttle in _previewThrottles.Values)
        {
            throttle.Flush();
        }
    }

    private void LoadControls()
    {
        GridLayoutRadio.IsChecked = Working.LayoutMode == SettingsOptionValues.GridLayout;
        CardLayoutRadio.IsChecked = Working.LayoutMode == SettingsOptionValues.CardLayout;
        if (GridLayoutRadio.IsChecked != true && CardLayoutRadio.IsChecked != true)
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
        GroupHoverDelaySlider.Value = Working.GroupHoverDelayMs;
        RefreshConfigLocationPanel();

        LayeredTransparencyRadio.IsChecked = Working.TransparencyMode == SettingsOptionValues.TransparencyLayered;
        AcrylicTransparencyRadio.IsChecked = Working.TransparencyMode == SettingsOptionValues.TransparencyAcrylic;
        WholeWindowTransparencyRadio.IsChecked = Working.TransparencyMode == SettingsOptionValues.TransparencyWholeWindow;
        OpacitySlider.Value = _viewModel.CurrentOpacity;

        SingleClickOpenRadio.IsChecked = Working.OpenItemsOnSingleClick;
        DoubleClickOpenRadio.IsChecked = !Working.OpenItemsOnSingleClick;
        GroupTransitionAnimationCheck.IsChecked = Working.GroupTransitionAnimation;
        SystemAnimationRadio.IsChecked = Working.AnimationMode == SettingsOptionValues.AnimationSystem;
        OnAnimationRadio.IsChecked = Working.AnimationMode == SettingsOptionValues.AnimationOn;
        OffAnimationRadio.IsChecked = Working.AnimationMode == SettingsOptionValues.AnimationOff;
        if (SystemAnimationRadio.IsChecked != true && OnAnimationRadio.IsChecked != true && OffAnimationRadio.IsChecked != true)
        {
            SystemAnimationRadio.IsChecked = true;
        }

        HotkeyBox.Text = Working.Hotkey;
        ScreenshotHotkeyBox.Text = Working.ScreenshotHotkey;
        RunAtStartupCheck.IsChecked = Working.StartWithWindows;
        OcrEnabledCheck.IsChecked = Working.OcrEnabled;
        OcrResultWindowCheck.IsChecked = Working.OcrShowResultWindow;
        FollowSystemThemeCheck.IsChecked = Working.FollowSystemTheme;
        RefreshOcrStatus();
        LoadColorControls();
        RefreshSkinList();
        RefreshSkinHint();
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
        FlushPreviewThrottles();
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
            OcrPanel,
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
            "extensions" => OcrPanel,
            "performance" => PerformancePanel,
            "about" => AboutPanel,
            _ => AppearancePanel,
        };
        selectedPanel.Visibility = Visibility.Visible;
        CategoryContentScrollViewer?.ScrollToTop();
    }

    private void ThemeColorChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || sender is not TextBox textBox) return;

        _viewModel.Update(settings =>
        {
            SetThemeColor(settings.ThemeColors, textBox.Name, textBox.Text);
            // 改色即脱离当前皮肤（已修改态），提示用户可另存为新皮肤。
            settings.ThemeProfileId = "";
            if (!string.Equals(settings.Theme, "custom", StringComparison.Ordinal))
            {
                settings.ThemeProfile = "自定义";
            }
            settings.Theme = "custom";
        });
        RefreshSkinHint();
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

    // ---------- 皮肤 ----------

    private void RefreshSkinList()
    {
        var selectedId = Working.ThemeProfileId;
        var cards = new List<SkinCardViewModel>();
        foreach (var preset in ThemePresets.All)
        {
            cards.Add(CreateSkinCard(preset, isBuiltIn: true, selectedId));
        }
        foreach (var custom in Working.CustomThemes)
        {
            cards.Add(CreateSkinCard(custom, isBuiltIn: false, selectedId));
        }
        SkinList.ItemsSource = cards;
    }

    private void RefreshSkinHint()
    {
        // ThemeProfileId 为空表示配色已脱离皮肤（改色未保存），提示可另存。
        SkinModifiedHint.Visibility = string.IsNullOrWhiteSpace(Working.ThemeProfileId)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static SkinCardViewModel CreateSkinCard(ThemeProfile skin, bool isBuiltIn, string selectedId)
    {
        var id = SkinApplier.ResolveSkinId(skin);
        return new SkinCardViewModel
        {
            SkinId = id,
            Name = skin.Name,
            Colors = skin.Colors,
            MaterialBadge = MaterialBadge(skin.Material, skin.Opacity),
            IsBuiltIn = isBuiltIn,
            IsSelected = string.Equals(id, selectedId, StringComparison.Ordinal),
        };
    }

    private static string MaterialBadge(string? material, double? opacity)
    {
        switch (material)
        {
            case "layered": return $"分层 {OpacityPercent(opacity, 1.0)}%";
            case SettingsOptionValues.TransparencyAcrylic: return $"玻璃 {OpacityPercent(opacity, 0.68)}%";
            case SettingsOptionValues.TransparencyWholeWindow: return $"整窗 {OpacityPercent(opacity, 0.85)}%";
            default: return "不透明";
        }
    }

    private static int OpacityPercent(double? opacity, double fallback) =>
        (int)Math.Round((opacity ?? fallback) * 100);

    private void SkinCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isLoading || sender is not FrameworkElement { Tag: SkinCardViewModel card }) return;
        var skin = SkinApplier.Resolve(card.SkinId, Working.CustomThemes);
        if (skin is null) return;

        _viewModel.Update(settings => SkinApplier.Apply(settings, skin));
        RunWhileLoading(() =>
        {
            LoadColorControls();
            RefreshOpacityControls();
            RefreshSkinList();
        });
        RefreshSkinHint();
        e.Handled = true;
    }

    private void SkinDelete_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_isLoading || sender is not FrameworkElement { Tag: SkinCardViewModel card } || card.IsBuiltIn) return;

        _viewModel.Update(settings =>
        {
            settings.CustomThemes.RemoveAll(t => string.Equals(t.Name, card.SkinId, StringComparison.Ordinal));
            // 删除的是当前皮肤时回落到内置深色皮肤。
            if (string.Equals(settings.ThemeProfileId, card.SkinId, StringComparison.Ordinal))
            {
                SkinApplier.Apply(settings, ThemePresets.FluentDark);
            }
        });
        RunWhileLoading(() =>
        {
            LoadColorControls();
            RefreshOpacityControls();
            RefreshSkinList();
        });
        RefreshSkinHint();
    }

    private void SaveSkin_Click(object sender, RoutedEventArgs e)
    {
        SaveSkinErrorText.Visibility = Visibility.Collapsed;
        SaveSkinNameBox.Text = Working.ThemeProfile is "自定义" or "自定义风格" ? "" : Working.ThemeProfile;
        SaveSkinOverlay.Visibility = Visibility.Visible;
        SaveSkinNameBox.Focus();
        SaveSkinNameBox.SelectAll();
    }

    private void SaveSkinCancel_Click(object sender, RoutedEventArgs e) => CloseSaveSkinOverlay();

    // 点击 scrim（对话框外的遮罩层本体）关闭；点对话框内部不关闭。
    private void SaveSkinOverlay_Background(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SaveSkinOverlay))
        {
            CloseSaveSkinOverlay();
        }
    }

    private void SaveSkinNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ConfirmSaveSkin();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseSaveSkinOverlay();
        }
    }

    private void SaveSkinConfirm_Click(object sender, RoutedEventArgs e) => ConfirmSaveSkin();

    private void ConfirmSaveSkin()
    {
        var name = SaveSkinNameBox.Text.Trim();
        if (name.Length == 0)
        {
            SaveSkinErrorText.Text = "请输入皮肤名称";
            SaveSkinErrorText.Visibility = Visibility.Visible;
            return;
        }

        // 重名自动追加序号，避免自定义皮肤同名混淆。
        var uniqueName = name;
        var existingNames = new HashSet<string>(Working.CustomThemes.Select(t => t.Name), StringComparer.Ordinal);
        var suffix = 2;
        while (!existingNames.Add(uniqueName))
        {
            uniqueName = $"{name} {suffix++}";
        }

        // 分层 100% 等价于不透明，保存为 opaque 皮肤以便徽标可读。
        var material = Working.TransparencyMode == SettingsOptionValues.TransparencyLayered
                       && Math.Abs(_viewModel.CurrentOpacity - 1.0) < 0.001
            ? "opaque"
            : Working.TransparencyMode;
        var skin = new ThemeProfile
        {
            Name = uniqueName,
            Colors = Working.ThemeColors.Clone(),
            Material = material,
            Opacity = _viewModel.CurrentOpacity,
        };

        _viewModel.Update(settings =>
        {
            settings.CustomThemes.Add(skin);
            settings.ThemeProfileId = SkinApplier.ResolveSkinId(skin);
            settings.ThemeProfile = uniqueName;
        });
        CloseSaveSkinOverlay();
        RefreshSkinList();
        RefreshSkinHint();
    }

    private void CloseSaveSkinOverlay() => SaveSkinOverlay.Visibility = Visibility.Collapsed;

    private sealed class SkinCardViewModel
    {
        public string SkinId { get; init; } = "";
        public string Name { get; init; } = "";
        public ThemeColors Colors { get; init; } = new();
        public string MaterialBadge { get; init; } = "";
        public bool IsBuiltIn { get; init; }
        public bool IsSelected { get; init; }
        public Visibility DeleteVisibility => IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;

        public System.Windows.Media.SolidColorBrush PanelBrush => ToBrush(Colors.Panel);
        public System.Windows.Media.SolidColorBrush SurfaceBrush => ToBrush(Colors.Surface);
        public System.Windows.Media.SolidColorBrush AccentBrush => ToBrush(Colors.Accent);
        public System.Windows.Media.SolidColorBrush TextPrimaryBrush => ToBrush(Colors.TextPrimary);
        public System.Windows.Media.SolidColorBrush HoverBrush => ToBrush(Colors.Hover);

        private static System.Windows.Media.SolidColorBrush ToBrush(string? hex)
        {
            try
            {
                var brush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex ?? "#808080"));
                if (brush.CanFreeze)
                {
                    brush.Freeze();
                }
                return brush;
            }
            catch
            {
                return System.Windows.Media.Brushes.Gray;
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
        FlushPreviewThrottles();
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
        if (_previewThrottles.TryGetValue(settingKey, out var throttle))
        {
            throttle.Push(slider.Value);
        }
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
                case "groupTransitionAnimation": settings.GroupTransitionAnimation = value; break;
                case "startWithWindows": settings.StartWithWindows = value; break;
                case "ocrEnabled": settings.OcrEnabled = value; break;
                case "ocrShowResultWindow": settings.OcrShowResultWindow = value; break;
                case "followSystemTheme":
                    settings.FollowSystemTheme = value;
                    if (value)
                    {
                        // 开启时立即按当前系统深浅色切换一次，让"跟随"即刻可见。
                        var skinId = SystemThemeWatcher.IsAppsLightTheme()
                            ? ThemePresets.FluentLightId
                            : ThemePresets.FluentDarkId;
                        var skin = SkinApplier.Resolve(skinId, settings.CustomThemes);
                        if (skin is not null)
                        {
                            SkinApplier.Apply(settings, skin);
                            RunWhileLoading(() =>
                            {
                                LoadColorControls();
                                RefreshOpacityControls();
                                RefreshSkinList();
                            });
                        }
                    }
                    break;
            }
        });
    }

    // ---------- 扩展与 OCR ----------

    private void RefreshOcrStatus()
    {
        var location = OcrBundleLocator.Locate();
        OcrStatusText.Text = location.Status switch
        {
            OcrBundleStatus.Installed =>
                $"已安装 v{location.Manifest?.Version}　{location.RootDirectory}",
            OcrBundleStatus.Corrupted =>
                "组件已损坏：" + string.Join("；", location.Problems.Take(2)) + "，可点击“下载 / 修复”重新安装。",
            _ =>
                "未安装。启用后首次取字会提示下载，也可在此提前安装或从文件导入。",
        };

        OcrUninstallButton.IsEnabled = location.Status != OcrBundleStatus.NotInstalled;
    }

    private void OcrInstall_Click(object sender, RoutedEventArgs e)
    {
        if (!Working.OcrEnabled)
        {
            OcrEnabledCheck.IsChecked = true;
        }

        var setup = new OcrSetupWindow { Owner = this };
        setup.ShowDialog();
        OcrEngineHost.Instance.Reset();
        RefreshOcrStatus();
    }

    private async void OcrImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 OCR 组件包",
            Filter = "OCR 组件包 (*.zip)|*.zip",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            OcrImportButton.IsEnabled = false;
            OcrStatusText.Text = "正在校验并安装…";
            await new OcrBundleService().InstallFromZipAsync(dialog.FileName, CancellationToken.None);
            OcrEngineHost.Instance.Reset();
        }
        catch (Exception ex)
        {
            OcrStatusText.Text = "导入失败：" + ex.Message;
            return;
        }
        finally
        {
            OcrImportButton.IsEnabled = true;
        }

        RefreshOcrStatus();
    }

    private void OcrUninstall_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "卸载后将删除用户目录下的 OCR 组件（约 35 MB），下次取字需重新下载或导入。继续？",
            "FlickD",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;

        OcrEngineHost.Instance.Reset();
        new OcrBundleService().Uninstall();
        RefreshOcrStatus();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        _previewThrottles["opacity"].Push(OpacitySlider.Value);
        RefreshValueLabels();
    }

    private void ContinuousSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is Slider { Tag: string settingKey } &&
            _previewThrottles.TryGetValue(settingKey, out var throttle))
        {
            throttle.Flush();
        }
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

        _previewThrottles["opacity"].Flush();
        _viewModel.UpdateCurrentOpacity(Math.Clamp(percent, 55, 100) / 100.0);
        RefreshOpacityControls();
    }

    private void ResetOpacity_Click(object sender, RoutedEventArgs e)
    {
        _previewThrottles["opacity"].Flush();
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
        IconSizeValue.Text = $"{IconSizeSlider.Value:0} DIP";
        CardWidthValue.Text = $"{CardWidthSlider.Value:0} DIP";
        CardHeightValue.Text = $"{CardHeightSlider.Value:0} DIP";
        TextSizeValue.Text = $"{TextSizeSlider.Value:0} pt";
        ItemSpacingValue.Text = $"{ItemSpacingSlider.Value:0} DIP";
        RowSpacingValue.Text = $"{RowSpacingSlider.Value:0} DIP";
        ContentPaddingValue.Text = $"{ContentPaddingSlider.Value:0} DIP";
        GroupLabelSizeValue.Text = $"{GroupLabelSizeSlider.Value:0} DIP";
        GroupLabelFontSizeValue.Text = $"{GroupLabelFontSizeSlider.Value:0} pt";
        GroupNavigationWidthValue.Text = $"{GroupNavigationWidthSlider.Value:0} DIP";
        GroupHoverDelayValue.Text = $"{(int)GroupHoverDelaySlider.Value} ms";
        OpacityPercentBox.Text = (OpacitySlider.Value * 100).ToString("0", CultureInfo.CurrentCulture);
    }

    private void RefreshConfigLocationPanel()
    {
        if (_maintenanceService is null)
        {
            ConfigPathText.Text = string.Empty;
            return;
        }

        var resolution = _maintenanceService.Resolve();
        ConfigPathText.Text = resolution.ConfigPath;
        RestoreConfigLocationButton.IsEnabled = !resolution.IsDefault;
        if (resolution.Warning is not null)
        {
            ConfigLocationStatusText.Text = SettingsMaintenanceMessages.DescribeWarning(resolution.Warning);
        }
    }

    private void CopyConfig_Click(object sender, RoutedEventArgs e)
    {
        if (_maintenanceService is null) return;
        try
        {
            _maintenanceService.CopyConfigPathToClipboard();
            ConfigLocationStatusText.Text = "已复制配置文件路径。";
        }
        catch (Exception ex)
        {
            ConfigLocationStatusText.Text = "复制失败：" + ex.Message;
        }
    }

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        if (_maintenanceService is null) return;
        try
        {
            _maintenanceService.OpenConfigLocation();
        }
        catch (Exception ex)
        {
            ConfigLocationStatusText.Text = "打开失败：" + ex.Message;
        }
    }

    private void ChangeConfigLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_maintenanceService is null) return;

        var dialog = new OpenFolderDialog
        {
            Title = "选择配置文件目录",
            Multiselect = false,
            InitialDirectory = _maintenanceService.ConfigDirectory,
        };
        if (dialog.ShowDialog(this) != true) return;

        ApplyLocationChange(() => _maintenanceService.ChangeLocation(dialog.FolderName, overwriteExisting: false),
            () => _maintenanceService.ChangeLocation(dialog.FolderName, overwriteExisting: true));
    }

    private void RestoreConfigLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_maintenanceService is null) return;

        ApplyLocationChange(() => _maintenanceService.RestoreDefaultLocation(overwriteExisting: false),
            () => _maintenanceService.RestoreDefaultLocation(overwriteExisting: true));
    }

    private void CreateConfigBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_maintenanceService is null) return;
        try
        {
            var backupPath = _maintenanceService.CreateBackup();
            ConfigLocationStatusText.Text = $"已创建完整配置备份：{backupPath}";
        }
        catch (Exception ex)
        {
            ConfigLocationStatusText.Text = "创建备份失败：" + ex.Message;
        }
    }

    private void ApplyLocationChange(
        Func<ConfigMigrationResult> attempt,
        Func<ConfigMigrationResult> retryWithOverwrite)
    {
        try
        {
            var result = attempt();
            if (result.Status == ConfigMigrationStatus.TargetContainsConfig)
            {
                var confirmed = ShowOverwriteConfirmDialog();
                if (!confirmed)
                {
                    ConfigLocationStatusText.Text = SettingsMaintenanceMessages.Describe(result);
                    return;
                }

                result = retryWithOverwrite();
            }

            ConfigLocationStatusText.Text = SettingsMaintenanceMessages.Describe(result);
            RefreshConfigLocationPanel();
        }
        catch (Exception ex)
        {
            ConfigLocationStatusText.Text = "操作失败：" + ex.Message;
        }
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

    private void ScreenshotHotkeyBox_KeyDown(object sender, KeyEventArgs e)
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

        if (string.Equals(normalized, Working.Hotkey, StringComparison.OrdinalIgnoreCase))
        {
            ScreenshotHotkeyBox.Text = Working.ScreenshotHotkey; // 与全局快捷键相同 → 还原不保存
            return;
        }

        _viewModel.Update(settings => settings.ScreenshotHotkey = normalized);
        ScreenshotHotkeyBox.Text = normalized;
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
        FlushPreviewThrottles();
        _ = _viewModel.Apply();
        CloseDecision = UnsavedCloseDecision.ApplyAndClose;
        Close();
    }

    public void CloseForOwnerShutdown()
    {
        CloseDecision = UnsavedCloseDecision.Discard;
        Close();
    }

    // 取消 = 放弃未保存修改并立即关闭，不进入“未保存设置”二次确认；
    // 右上角 X / Esc 仍由 Window_Closing 走确认流程。
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CloseDecision = UnsavedCloseDecision.Discard;
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        FlushPreviewThrottles();
        if (CloseDecision != UnsavedCloseDecision.KeepEditing)
        {
            return;
        }

        if (!_viewModel.HasChanges)
        {
            CloseDecision = UnsavedCloseDecision.Discard;
            return;
        }

        var decision = ShowUnsavedChangesDialog();
        if (decision == UnsavedCloseDecision.KeepEditing)
        {
            e.Cancel = true;
            return;
        }

        CloseDecision = decision;
    }

    private UnsavedCloseDecision ShowUnsavedChangesDialog()
    {
        var decision = UnsavedCloseDecision.KeepEditing;
        var dialog = new Window
        {
            Title = "\u672A\u4FDD\u5B58\u7684\u8BBE\u7F6E",
            Owner = this,
            Width = 440,
            Height = 210,
            MinWidth = 440,
            MinHeight = 210,
            MaxWidth = 440,
            MaxHeight = 210,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        dialog.SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        dialog.SetResourceReference(ForegroundProperty, "PrimaryTextBrush");

        var title = new TextBlock
        {
            Text = "\u8981\u4FDD\u5B58\u8FD9\u4E9B\u8BBE\u7F6E\u66F4\u6539\u5417\uFF1F",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
        };
        var description = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 20),
            Text = "\u5E94\u7528\u5E76\u5173\u95ED\u4F1A\u5199\u5165\u914D\u7F6E\uFF1B\u653E\u5F03\u66F4\u6539\u4F1A\u6062\u590D\u6253\u5F00\u8BBE\u7F6E\u524D\u7684\u72B6\u6001\u3002",
            TextWrapping = TextWrapping.Wrap,
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");

        var keepEditingButton = CreateCloseDecisionButton("\u7EE7\u7EED\u7F16\u8F91", isDefault: false);
        keepEditingButton.IsCancel = true;
        keepEditingButton.Click += (_, _) =>
        {
            decision = UnsavedCloseDecision.KeepEditing;
            dialog.DialogResult = false;
        };

        var discardButton = CreateCloseDecisionButton("\u653E\u5F03\u66F4\u6539", isDefault: false);
        discardButton.Click += (_, _) =>
        {
            decision = UnsavedCloseDecision.Discard;
            dialog.DialogResult = true;
        };

        var applyButton = CreateCloseDecisionButton("\u5E94\u7528\u5E76\u5173\u95ED", isDefault: true);
        applyButton.Click += (_, _) =>
        {
            decision = UnsavedCloseDecision.ApplyAndClose;
            dialog.DialogResult = true;
        };

        var buttons = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Orientation = Orientation.Horizontal,
        };
        buttons.Children.Add(keepEditingButton);
        buttons.Children.Add(discardButton);
        buttons.Children.Add(applyButton);

        var content = new Grid { Margin = new Thickness(24) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(title);
        Grid.SetRow(description, 1);
        content.Children.Add(description);
        Grid.SetRow(buttons, 2);
        content.Children.Add(buttons);
        dialog.Content = content;
        _ = dialog.ShowDialog();
        return decision;
    }

    private bool ShowOverwriteConfirmDialog()
    {
        var confirmed = false;
        var dialog = new Window
        {
            Title = "更换配置位置",
            Owner = this,
            Width = 440,
            Height = 200,
            MinWidth = 440,
            MinHeight = 200,
            MaxWidth = 440,
            MaxHeight = 200,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        dialog.SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        dialog.SetResourceReference(ForegroundProperty, "PrimaryTextBrush");

        var title = new TextBlock
        {
            Text = "目标目录已存在 config.json，要覆盖吗？",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var description = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 20),
            Text = "覆盖前会在目标目录生成带时间戳的备份文件；原目录的配置文件保持不变。",
            TextWrapping = TextWrapping.Wrap,
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");

        var cancelButton = CreateCloseDecisionButton("取消", isDefault: false);
        cancelButton.IsCancel = true;
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        var overwriteButton = CreateCloseDecisionButton("覆盖并备份", isDefault: true);
        overwriteButton.Click += (_, _) =>
        {
            confirmed = true;
            dialog.DialogResult = true;
        };

        var buttons = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Orientation = Orientation.Horizontal,
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(overwriteButton);

        var content = new Grid { Margin = new Thickness(24) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(title);
        Grid.SetRow(description, 1);
        content.Children.Add(description);
        Grid.SetRow(buttons, 2);
        content.Children.Add(buttons);
        dialog.Content = content;
        _ = dialog.ShowDialog();
        return confirmed;
    }

    private Button CreateCloseDecisionButton(string content, bool isDefault)
    {
        var button = new Button
        {
            Content = content,
            MinWidth = 96,
            Height = 34,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = isDefault,
        };
        if (isDefault && TryFindResource("PrimaryButtonStyle") is Style primaryStyle)
        {
            button.Style = primaryStyle;
        }
        return button;
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
            if (!string.IsNullOrEmpty(info.Error))
            {
                CheckUpdateButton.IsEnabled = true;
                UpdateStatusText.Text = info.Error + "，请稍后重试或前往开源地址手动下载。";
                return;
            }

            if (!info.HasUpdate)
            {
                CheckUpdateButton.IsEnabled = true;
                UpdateStatusText.Text = $"已是最新版本 v{info.CurrentVersion}";
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
            await _updateService.DownloadAndApplyAsync(info.DownloadUrl, info.AssetName, info.AssetSize, info.AssetSha256, progress, CancellationToken.None);
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
