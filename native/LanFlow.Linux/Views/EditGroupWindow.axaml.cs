using Avalonia;
using Avalonia.Controls;

namespace LanFlow.Desktop.Views;

public sealed partial class EditGroupWindow : Window
{
    public static readonly StyledProperty<string> GroupNameProperty =
        AvaloniaProperty.Register<EditGroupWindow, string>(nameof(GroupName), string.Empty);

    public string GroupName
    {
        get => GetValue(GroupNameProperty);
        set => SetValue(GroupNameProperty, value);
    }

    public bool Confirmed { get; private set; }

    public EditGroupWindow()
    {
        InitializeComponent();
    }

    private void OnConfirm(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GroupName))
        {
            GroupName = "未命名分组";
        }

        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}
