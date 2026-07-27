using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop.Views;

public partial class SettingsWindow : Window
{
    private readonly Settings _working;
    private bool _isLoading = true;

    public SettingsWindow(Settings settings)
    {
        _working = Clone(settings);
        InitializeComponent();
        LoadControls();
        _isLoading = false;
    }

    public Settings Result => Clone(_working);
    public event Action<Settings>? PreviewChanged;

    private void LoadControls()
    {
        ThemePresetCombo.SelectedIndex = _working.Theme == "light" ? 1 : _working.Theme == "custom" ? 2 : 0;
        LayoutModeToggle.State = _working.LayoutMode == "card";
        ShowTitleToggle.State = _working.ShowItemTitle;
        SingleClickOpenRadio.IsChecked = _working.OpenItemsOnSingleClick;
        DoubleClickOpenRadio.IsChecked = !_working.OpenItemsOnSingleClick;
        IconSizeSlider.Value = _working.IconSize;
        CardWidthSlider.Value = _working.CardWidth;
        CardHeightSlider.Value = _working.CardHeight;
        TextSizeSlider.Value = _working.TextSize;
        ItemSpacingSlider.Value = _working.ItemSpacing;
        RowSpacingSlider.Value = _working.RowSpacing;
        OpacitySlider.Value = _working.Opacity;
        GroupLayoutToggle.State = _working.GroupLayout == "top";
        HotkeyBox.Text = _working.Hotkey;
        RunAtStartupCheck.IsChecked = _working.StartWithWindows;
        LoadColorControls();
        RefreshValueLabels();
        VersionText.Text = "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.5");
    }

    private void LoadColorControls()
    {
        var c = _working.ThemeColors;
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
        _working.Theme = theme;
        _working.ThemeProfile = theme == "light" ? "浅色" : theme == "dark" ? "深色" : "自定义";
        _working.ThemeColors = theme == "light" ? ThemeColors.Light() : theme == "dark" ? ThemeColors.Dark() : Clone(_working.ThemeColors);
        _isLoading = true;
        LoadColorControls();
        _isLoading = false;
        Preview();
    }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _working.LayoutMode = LayoutModeToggle.State ? "card" : "tile";
        _working.ShowItemTitle = ShowTitleToggle.State;
        _working.OpenItemsOnSingleClick = SingleClickOpenRadio.IsChecked == true;
        _working.IconSize = IconSizeSlider.Value;
        _working.CardWidth = CardWidthSlider.Value;
        _working.CardHeight = CardHeightSlider.Value;
        _working.TextSize = TextSizeSlider.Value;
        _working.ItemSpacing = ItemSpacingSlider.Value;
        _working.RowSpacing = RowSpacingSlider.Value;
        _working.Opacity = OpacitySlider.Value;
        _working.GroupLayout = GroupLayoutToggle.State ? "top" : "left";
        _working.StartWithWindows = RunAtStartupCheck.IsChecked == true;
        RefreshValueLabels();
        Preview();
    }

    private void ThemeColorChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        var c = _working.ThemeColors;
        c.Panel = PanelColorBox.Text; c.PanelBorder = PanelBorderColorBox.Text; c.Surface = SurfaceColorBox.Text;
        c.SurfaceBorder = SurfaceBorderColorBox.Text; c.Footer = FooterColorBox.Text;
        c.TextPrimary = PrimaryTextColorBox.Text; c.TextSecondary = SecondaryTextColorBox.Text; c.Accent = AccentColorBox.Text;
        c.Hover = HoverColorBox.Text; c.IconSurface = IconSurfaceColorBox.Text;
        _working.Theme = "custom";
        _working.ThemeProfile = "自定义";
        RefreshColorPickerButtons();
        Preview();
    }

    private void RefreshValueLabels()
    {
        IconSizeValue.Text = $"{_working.IconSize:0}";
        CardWidthValue.Text = $"{_working.CardWidth:0}";
        CardHeightValue.Text = $"{_working.CardHeight:0}";
        TextSizeValue.Text = $"{_working.TextSize:0}";
        OpacityValue.Text = $"{_working.Opacity:P0}";
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
        _working.Hotkey = normalized;
        HotkeyBox.Text = normalized;
        Preview();
    }

    private void Preview() => PreviewChanged?.Invoke(Result);
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

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private static Settings Clone(Settings value) => new()
    {
        Hotkey = value.Hotkey, Theme = value.Theme, ThemeProfile = value.ThemeProfile, ThemeColors = Clone(value.ThemeColors),
        CustomThemes = value.CustomThemes.Select(p => new ThemeProfile { Name = p.Name, Colors = Clone(p.Colors) }).ToList(), Opacity = value.Opacity,
        LayoutMode = value.LayoutMode, IconSize = value.IconSize, CardWidth = value.CardWidth, CardHeight = value.CardHeight, TextSize = value.TextSize, ItemSpacing = value.ItemSpacing,
        RowSpacing = value.RowSpacing, ContentPadding = value.ContentPadding, ShowShortcutBadge = value.ShowShortcutBadge, ShowItemTitle = value.ShowItemTitle,
        ShowFullItemName = value.ShowFullItemName, GroupLayout = value.GroupLayout, StartWithWindows = value.StartWithWindows,
        OpenItemsOnSingleClick = value.OpenItemsOnSingleClick,
    };

    private static ThemeColors Clone(ThemeColors c) => new()
    {
        Panel = c.Panel, PanelBorder = c.PanelBorder, Surface = c.Surface, SurfaceBorder = c.SurfaceBorder, Footer = c.Footer,
        TextPrimary = c.TextPrimary, TextSecondary = c.TextSecondary, Accent = c.Accent, Hover = c.Hover, IconSurface = c.IconSurface
    };
}
