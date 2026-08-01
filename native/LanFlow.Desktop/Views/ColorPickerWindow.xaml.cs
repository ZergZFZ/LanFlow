using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DrawingColor = System.Drawing.Color;

namespace LanFlow.Desktop.Views;

public partial class ColorPickerWindow : Window
{
    private double _hue;
    private double _saturation;
    private double _value;
    private bool _updating;

    public ColorPickerWindow(string initialColor)
    {
        InitializeComponent();
        if (!TryParse(initialColor, out var color)) color = Colors.White;
        SetFromColor(color);
        Loaded += (_, _) => UpdateVisuals();
    }

    public string SelectedColor { get; private set; } = "#FFFFFF";

    private void DialogHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Spectrum_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Spectrum.CaptureMouse();
        UpdateSpectrum(e.GetPosition(Spectrum));
        e.Handled = true;
    }

    private void Spectrum_MouseMove(object sender, MouseEventArgs e)
    {
        if (Spectrum.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateSpectrum(e.GetPosition(Spectrum));
        }
    }

    private void HueBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HueBar.CaptureMouse();
        UpdateHue(e.GetPosition(HueBar).X);
        e.Handled = true;
    }

    private void HueBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (HueBar.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateHue(e.GetPosition(HueBar).X);
        }
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ReleaseColorCapture();

    private void Window_Deactivated(object? sender, EventArgs e) => ReleaseColorCapture();

    private void ReleaseColorCapture()
    {
        if (Spectrum.IsMouseCaptured) Spectrum.ReleaseMouseCapture();
        if (HueBar.IsMouseCaptured) HueBar.ReleaseMouseCapture();
    }

    private void UpdateSpectrum(Point position)
    {
        _saturation = Math.Clamp(position.X / Math.Max(1, Spectrum.ActualWidth), 0, 1);
        _value = Math.Clamp(1 - position.Y / Math.Max(1, Spectrum.ActualHeight), 0, 1);
        UpdateVisuals();
    }

    private void UpdateHue(double position)
    {
        _hue = Math.Clamp(position / Math.Max(1, HueBar.ActualWidth), 0, 1) * 360;
        UpdateVisuals();
    }

    private void HexBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating || !TryParse(HexBox.Text, out var color)) return;
        SetFromColor(color);
        UpdateVisuals();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var color = HsvToColor(_hue, _saturation, _value);
        SelectedColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        DialogResult = true;
    }

    private void SetFromColor(Color color)
    {
        var drawing = DrawingColor.FromArgb(color.A, color.R, color.G, color.B);
        _hue = drawing.GetHue();
        _saturation = drawing.GetSaturation();
        _value = drawing.GetBrightness();
    }

    private void UpdateVisuals()
    {
        if (!IsLoaded) return;
        var hueColor = HsvToColor(_hue, 1, 1);
        var selected = HsvToColor(_hue, _saturation, _value);
        HueStop.Color = hueColor;
        PreviewSwatch.Background = new SolidColorBrush(selected);
        Canvas.SetLeft(SpectrumMarker, Math.Clamp(_saturation * Spectrum.ActualWidth - 8, -8, Spectrum.ActualWidth - 8));
        Canvas.SetTop(SpectrumMarker, Math.Clamp((1 - _value) * Spectrum.ActualHeight - 8, -8, Spectrum.ActualHeight - 8));
        HueMarker.Margin = new Thickness(Math.Clamp(_hue / 360 * HueBar.ActualWidth - 2, 0, HueBar.ActualWidth - 4), 0, 0, 0);
        _updating = true;
        HexBox.Text = $"#{selected.R:X2}{selected.G:X2}{selected.B:X2}";
        _updating = false;
    }

    private static bool TryParse(string value, out Color color)
    {
        try { color = (Color)ColorConverter.ConvertFromString(value); return true; }
        catch (FormatException) { color = Colors.White; return false; }
    }

    private static Color HsvToColor(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var segment = hue / 60;
        var x = chroma * (1 - Math.Abs(segment % 2 - 1));
        var (r, g, b) = segment switch
        {
            < 1 => (chroma, x, 0d), < 2 => (x, chroma, 0d), < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma), < 5 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        var offset = value - chroma;
        return Color.FromRgb((byte)Math.Round((r + offset) * 255), (byte)Math.Round((g + offset) * 255), (byte)Math.Round((b + offset) * 255));
    }
}
