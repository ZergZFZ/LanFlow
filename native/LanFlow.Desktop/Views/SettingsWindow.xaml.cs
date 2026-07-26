using System.Windows;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(Settings settings)
    {
        InitializeComponent();

        DarkThemeRadio.IsChecked = settings.Theme != "light";
        LightThemeRadio.IsChecked = settings.Theme == "light";
        ShowShortcutBadgeCheckBox.IsChecked = settings.ShowShortcutBadge;
        ShowFullItemNameCheckBox.IsChecked = settings.ShowFullItemName;
        OpacitySlider.Value = Math.Clamp(settings.Opacity * 100, 55, 100);
        LeftGroupLayoutRadio.IsChecked = settings.GroupLayout != "top";
        TopGroupLayoutRadio.IsChecked = settings.GroupLayout == "top";
    }

    public string Theme => LightThemeRadio.IsChecked == true ? "light" : "dark";

    public double PanelOpacity => OpacitySlider.Value / 100;

    public bool ShowShortcutBadge => ShowShortcutBadgeCheckBox.IsChecked == true;

    public bool ShowFullItemName => ShowFullItemNameCheckBox.IsChecked == true;

    public string GroupLayout => TopGroupLayoutRadio.IsChecked == true ? "top" : "left";

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
