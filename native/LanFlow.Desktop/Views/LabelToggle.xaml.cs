using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LanFlow.Desktop.Views;

public partial class LabelToggle : UserControl
{
    public static readonly DependencyProperty LeftTextProperty =
        DependencyProperty.Register(nameof(LeftText), typeof(string), typeof(LabelToggle),
            new PropertyMetadata(string.Empty, (o, e) => ((LabelToggle)o).LeftLabel.Text = (string)e.NewValue));

    public static readonly DependencyProperty RightTextProperty =
        DependencyProperty.Register(nameof(RightText), typeof(string), typeof(LabelToggle),
            new PropertyMetadata(string.Empty, (o, e) => ((LabelToggle)o).RightLabel.Text = (string)e.NewValue));

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(bool), typeof(LabelToggle),
            new PropertyMetadata(false, OnStateChanged));

    public static readonly RoutedEvent StateChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(StateChanged), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(LabelToggle));

    public string LeftText
    {
        get => (string)GetValue(LeftTextProperty);
        set => SetValue(LeftTextProperty, value);
    }

    public string RightText
    {
        get => (string)GetValue(RightTextProperty);
        set => SetValue(RightTextProperty, value);
    }

    public bool State
    {
        get => (bool)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public event RoutedEventHandler StateChanged
    {
        add => AddHandler(StateChangedEvent, value);
        remove => RemoveHandler(StateChangedEvent, value);
    }

    private readonly TranslateTransform _knobTransform = new();
    private const double KnobTravel = 24;

    public LabelToggle()
    {
        InitializeComponent();
        Knob.RenderTransform = _knobTransform;
        UpdateVisual(true);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LabelToggle)d).UpdateVisual(false);

    private void LeftLabel_Click(object sender, MouseButtonEventArgs e) => SetState(false);
    private void RightLabel_Click(object sender, MouseButtonEventArgs e) => SetState(true);
    private void Track_Click(object sender, MouseButtonEventArgs e) => SetState(!State);

    private void SetState(bool value)
    {
        if (State == value) return;
        State = value;
        RaiseEvent(new RoutedEventArgs(StateChangedEvent));
    }

    private void UpdateVisual(bool immediate)
    {
        var target = State ? KnobTravel : 0;
        var dim = new SolidColorBrush(Color.FromRgb(0xAE, 0xB7, 0xCB));
        var bright = new SolidColorBrush(Colors.White);

        LeftLabel.FontWeight = State ? FontWeights.Normal : FontWeights.SemiBold;
        RightLabel.FontWeight = State ? FontWeights.SemiBold : FontWeights.Normal;
        LeftLabel.Foreground = State ? dim : bright;
        RightLabel.Foreground = State ? bright : dim;
        Track.Background = State
            ? new SolidColorBrush(Color.FromRgb(0x52, 0x6F, 0xAF))
            : new SolidColorBrush(Color.FromRgb(0x38, 0x42, 0x5B));

        if (immediate)
        {
            _knobTransform.X = target;
            return;
        }

        var anim = new DoubleAnimation(target, new Duration(TimeSpan.FromMilliseconds(140)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _knobTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}