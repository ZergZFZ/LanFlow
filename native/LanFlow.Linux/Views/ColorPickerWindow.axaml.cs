using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;

namespace LanFlow.Desktop.Views;

public sealed partial class ColorPickerWindow : Window
{
    private double _hue;
    private double _sat;
    private double _val;
    private bool _updating;
    private bool _svPressed;
    private bool _confirmed;

    public ColorPickerWindow()
    {
        InitializeComponent();
        SvOverlay.PointerPressed += OnSvPointerPressed;
        SvOverlay.PointerMoved += OnSvPointerMoved;
        SvOverlay.PointerReleased += OnSvPointerReleased;
        SvOverlay.PointerExited += (_, _) => _svPressed = false;
        HueSlider.ValueChanged += (_, _) => OnHueChanged();
        HexBox.TextChanged += (_, _) => OnHexChanged();
        RedBox.ValueChanged += (_, _) => OnRgbChanged();
        GreenBox.ValueChanged += (_, _) => OnRgbChanged();
        BlueBox.ValueChanged += (_, _) => OnRgbChanged();
    }

    public bool Confirmed => _confirmed;

    public string ResultColor => ToHex(FromHsv(_hue, _sat, _val));

    public void Initialize(string hex)
    {
        (byte r, byte g, byte b) = ParseHex(hex);
        var (h, s, v) = RgbToHsv(r, g, b);
        _hue = h;
        _sat = s;
        _val = v;
        HueSlider.Value = h;
        UpdateSvBase();
        UpdateIndicator();
        SyncControls(r, g, b);
    }

    private void OnSvPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _svPressed = true;
        UpdateFromPointer(e);
    }

    private void OnSvPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_svPressed)
        {
            UpdateFromPointer(e);
        }
    }

    private void OnSvPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _svPressed = false;
    }

    private void UpdateFromPointer(PointerEventArgs e)
    {
        var point = e.GetPosition(SvCanvas);
        _sat = Math.Clamp(point.X / SvCanvas.Width, 0, 1);
        _val = Math.Clamp(1 - point.Y / SvCanvas.Height, 0, 1);
        UpdateIndicator();
        SyncControls(FromHsv(_hue, _sat, _val));
    }

    private void UpdateIndicator()
    {
        Canvas.SetLeft(SvIndicator, _sat * SvCanvas.Width - 6);
        Canvas.SetTop(SvIndicator, (1 - _val) * SvCanvas.Height - 6);
    }

    private void UpdateSvBase()
    {
        var pure = FromHsv(_hue, 1, 1);
        SvHueBase.Background = new SolidColorBrush(Color.FromRgb(pure.r, pure.g, pure.b));
    }

    private void OnHueChanged()
    {
        _hue = HueSlider.Value;
        UpdateSvBase();
        SyncControls(FromHsv(_hue, _sat, _val));
    }

    private void OnHexChanged()
    {
        if (_updating || string.IsNullOrWhiteSpace(HexBox.Text))
        {
            return;
        }

        var hex = HexBox.Text.Trim();
        if (!hex.StartsWith("#", StringComparison.OrdinalIgnoreCase))
        {
            hex = "#" + hex;
        }

        if (hex.Length != 7)
        {
            return;
        }

        try
        {
            var color = Color.Parse(hex);
            var (h, s, v) = RgbToHsv(color.R, color.G, color.B);
            _hue = h;
            _sat = s;
            _val = v;
            HueSlider.Value = h;
            UpdateSvBase();
            UpdateIndicator();
            SyncControls(color.R, color.G, color.B);
        }
        catch
        {
            // ignore invalid input
        }
    }

    private void OnRgbChanged()
    {
        if (_updating)
        {
            return;
        }

        var (h, s, v) = RgbToHsv((byte)RedBox.Value, (byte)GreenBox.Value, (byte)BlueBox.Value);
        _hue = h;
        _sat = s;
        _val = v;
        HueSlider.Value = h;
        UpdateSvBase();
        UpdateIndicator();
        SyncControls((byte)RedBox.Value, (byte)GreenBox.Value, (byte)BlueBox.Value);
    }

    private void SyncControls((byte r, byte g, byte b) color) => SyncControls(color.r, color.g, color.b);

    private void SyncControls(byte r, byte g, byte b)
    {
        _updating = true;
        Preview.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        if (!HexBox.IsFocused)
        {
            HexBox.Text = ToHex((r, g, b));
        }

        if (!RedBox.IsFocused) RedBox.Value = r;
        if (!GreenBox.IsFocused) GreenBox.Value = g;
        if (!BlueBox.IsFocused) BlueBox.Value = b;
        _updating = false;
    }

    private void OnConfirm(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(HexBox.Text))
        {
            HexBox.Text = ResultColor;
        }
        else
        {
            OnHexChanged();
        }

        _confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _confirmed = false;
        Close();
    }

    private static (byte r, byte g, byte b) FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        var m = v - c;
        double r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        return ((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }

    private static (double h, double s, double v) RgbToHsv(byte r, byte g, byte b)
    {
        var rr = r / 255.0;
        var gg = g / 255.0;
        var bb = b / 255.0;
        var max = Math.Max(rr, Math.Max(gg, bb));
        var min = Math.Min(rr, Math.Min(gg, bb));
        var d = max - min;
        double h;
        if (d == 0)
        {
            h = 0;
        }
        else if (max == rr)
        {
            h = 60 * (((gg - bb) / d) % 6);
        }
        else if (max == gg)
        {
            h = 60 * ((bb - rr) / d + 2);
        }
        else
        {
            h = 60 * ((rr - gg) / d + 4);
        }

        if (h < 0)
        {
            h += 360;
        }

        var s = max == 0 ? 0 : d / max;
        return (h, s, max);
    }

    private static string ToHex((byte r, byte g, byte b) color) =>
        $"#{color.r:X2}{color.g:X2}{color.b:X2}";

    private static (byte r, byte g, byte b) ParseHex(string hex)
    {
        try
        {
            var color = Color.Parse(hex.StartsWith("#", StringComparison.OrdinalIgnoreCase) ? hex : "#" + hex);
            return (color.R, color.G, color.B);
        }
        catch
        {
            return (0, 0, 0);
        }
    }
}
