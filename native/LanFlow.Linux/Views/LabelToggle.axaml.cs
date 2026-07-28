using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace LanFlow.Desktop.Views;

public partial class LabelToggle : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<LabelToggle, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<LabelToggle, string>(nameof(Description), string.Empty);

    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<LabelToggle, bool>(nameof(IsChecked));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public event EventHandler? IsCheckedChanged;

    static LabelToggle()
    {
        IsCheckedProperty.Changed.AddClassHandler<LabelToggle>((sender, e) => sender.OnIsCheckedChanged(e));
    }

    private void OnIsCheckedChanged(AvaloniaPropertyChangedEventArgs e)
    {
        IsCheckedChanged?.Invoke(this, EventArgs.Empty);
    }
}
