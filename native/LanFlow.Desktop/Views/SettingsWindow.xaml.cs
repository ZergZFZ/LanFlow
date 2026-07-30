using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Presentation;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsPreviewSession _session;
    private bool _isLoading = true;
    private readonly UpdateService _updateService = new();

    public SettingsWindow(SettingsPreviewSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        InitializeComponent();
        LoadControls();
        _isLoading = false;
    }

    private Settings Working => _session.Working;

    private void LoadControls()
    {
        ThemePresetCombo.SelectedIndex = Working.Theme == "light" ? 1 : Working.Theme == "custom" ? 2 : 0;
        LayoutModeToggle.State = Working.LayoutMode == "card";
        ShowTitleToggle.State = Working.ShowItemTitle;
        SingleClickOpenRadio.IsChecked = Working.OpenItemsOnSingleClick;
        DoubleClickOpenRadio.IsChecked = !Working.OpenItemsOnSingleClick;
        IconSizeSlider.Value = Working.IconSize;
        CardWidthSlider.Value = Working.CardWidth;
        CardHeightSlider.Value = Working.CardHeight;
        TextSizeSlider.Value = Working.TextSize;
        ItemSpacingSlider.Value = Working.ItemSpacing;
        RowSpacingSlider.Value = Working.RowSpacing;
        OpacitySlider.Value = Working.Opacity;
        GroupLayoutToggle.State = Working.GroupLayout == "top";
        HotkeyBox.Text = Working.Hotkey;
        RunAtStartupCheck.IsChecked = Working.StartWithWindows;
        LoadColorControls();
        RefreshValueLabels();
        VersionText.Text = "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.5");
    }

    private void LoadColorControls()
    {
        var c = Working.ThemeColors;
        PanelColorBox.Text = c.Panel; PanelBorderColorBox.Text = c.PanelBorder; SurfaceColorBox.Text = c.Surface;
        SurfaceBorderColorBox.Text = c.SurfaceBorder; FooterColorBox.Text = c.Footer;
        PrimaryTextColorBox.Text = c.TextPrimary; SecondaryTextColorBox.Text = c.TextSecondary; AccentColorBox.Text = c.Accent;
        HoverColorBox.Text = c.Hover; IconSurfaceColorBox.Text = c.IconSurface;
        RefreshColorPickerButtons();
    }

    private void RefreshColorPickerButtons()
    {
        foreach (var button in FindVisualChildren<Button>(this).Where(button => button.Tag is string))
        {
            var name = (string)button.Tag;
            if (FindName(name) is not TextBox textBox) continue;
            if (FindName(name + "Swatch") is not System.Windows.Controls.Border swatch) continue;
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(textBox.Text);
                swatch.Background = new System.Windows.Media.SolidColorBrush(color);
            }
            catch (FormatException)
            {
                swatch.ClearValue(System.Windows.Controls.Border.BackgroundProperty);
            }
        }
    }

    private void ColorPicker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } || FindName(name) is not TextBox textBox) return;
        var picker = new ColorPickerWindow(textBox.Text) { Owner = this };
        if (picker.ShowDialog() == true) textBox.Text = picker.SelectedColor;
    }

    private void ThemePresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ThemePresetCombo.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
        _session.Update(settings =>
        {
            settings.Theme = theme;
            settings.ThemeProfile = theme == "light" ? "浅色" : theme == "dark" ? "深色" : "自定义";
            settings.ThemeColors = theme == "light"
                ? ThemeColors.Light()
                : theme == "dark"
                    ? ThemeColors.Dark()
                    : settings.ThemeColors.Clone();
        });
        _isLoading = true;
        LoadColorControls();
        _isLoading = false;
    }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _session.Update(settings =>
        {
            LegacySettingsControlMapper.ApplyLayoutToggle(
                settings,
                LayoutModeToggle.State,
                ReferenceEquals(sender, LayoutModeToggle));
            settings.ShowItemTitle = ShowTitleToggle.State;
            settings.OpenItemsOnSingleClick = SingleClickOpenRadio.IsChecked == true;
            settings.IconSize = IconSizeSlider.Value;
            settings.CardWidth = CardWidthSlider.Value;
            settings.CardHeight = CardHeightSlider.Value;
            settings.TextSize = TextSizeSlider.Value;
            settings.ItemSpacing = ItemSpacingSlider.Value;
            settings.RowSpacing = RowSpacingSlider.Value;
            LegacySettingsControlMapper.ApplyOpacity(settings, OpacitySlider.Value);
            settings.GroupLayout = GroupLayoutToggle.State ? "top" : "left";
            settings.StartWithWindows = RunAtStartupCheck.IsChecked == true;
        });
        RefreshValueLabels();
    }

    private void ThemeColorChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        _session.Update(settings =>
        {
            var colors = settings.ThemeColors;
            colors.Panel = PanelColorBox.Text; colors.PanelBorder = PanelBorderColorBox.Text;
            colors.Surface = SurfaceColorBox.Text; colors.SurfaceBorder = SurfaceBorderColorBox.Text; colors.Footer = FooterColorBox.Text;
            colors.TextPrimary = PrimaryTextColorBox.Text; colors.TextSecondary = SecondaryTextColorBox.Text; colors.Accent = AccentColorBox.Text;
            colors.Hover = HoverColorBox.Text; colors.IconSurface = IconSurfaceColorBox.Text;
            settings.Theme = "custom";
            settings.ThemeProfile = "自定义";
        });
        RefreshColorPickerButtons();
    }

    private void RefreshValueLabels()
    {
        IconSizeValue.Text = $"{Working.IconSize:0}";
        CardWidthValue.Text = $"{Working.CardWidth:0}";
        CardHeightValue.Text = $"{Working.CardHeight:0}";
        TextSizeValue.Text = $"{Working.TextSize:0}";
        OpacityValue.Text = $"{Working.Opacity:P0}";
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
        _session.Update(settings => settings.Hotkey = normalized);
        HotkeyBox.Text = normalized;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void AboutLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 忽略无法打开浏览器的情况
        }
        e.Handled = true;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "检查更新";
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
            var progress = new Progress<double>(p => UpdateStatusText.Text = $"正在下载更新… {p:P0}");
            await _updateService.DownloadAndApplyAsync(info.DownloadUrl, info.AssetName, progress, CancellationToken.None);
            // 不会返回：DownloadAndApplyAsync 内部会结束当前进程并重启。
        }
        catch (Exception ex)
        {
            CheckUpdateButton.IsEnabled = true;
            CheckUpdateButton.Content = "检查更新";
            UpdateStatusText.Text = "更新失败：" + ex.Message + "（可前往开源地址手动下载）";
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
