using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop.Views;

public sealed partial class EditItemWindow : Window
{
    private readonly ShortcutService _shortcutService = new();
    private LauncherItem? _item;
    private bool _confirmed;

    public EditItemWindow()
    {
        InitializeComponent();
    }

    public void InitializeDialog(LauncherItem item)
    {
        _item = item;
        TypeBox.SelectedIndex = item.IsCommand ? 1 : 0;
        NameBox.Text = item.Name;
        PathBox.Text = item.Path;
        CommandBox.Text = item.Command;
        IconBox.Text = item.Icon;
        HotkeyBox.Text = item.Hotkey;
        EnabledBox.IsChecked = item.IsEnabled;
        ApplyTypeVisibility();
    }

    public bool Confirmed => _confirmed;

    private bool IsCommand => TypeBox.SelectedIndex == 1;

    private void ApplyTypeVisibility()
    {
        PathRow.IsVisible = !IsCommand;
        IconRow.IsVisible = !IsCommand;
        CommandBox.IsVisible = IsCommand;
    }

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs e) => ApplyTypeVisibility();

    private async void OnBrowsePath(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择程序或快捷方式",
            AllowMultiple = false,
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file is null)
        {
            return;
        }

        var path = file.Path.LocalPath;
        PathBox.Text = path;

        if (path.EndsWith(".desktop", System.StringComparison.OrdinalIgnoreCase))
        {
            var (name, _, _) = ShellIconService.ParseDesktop(path);
            if (string.IsNullOrWhiteSpace(NameBox.Text) && !string.IsNullOrWhiteSpace(name))
            {
                NameBox.Text = name;
            }
        }
    }

    private async void OnBrowseIcon(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择图标",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("图像")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.ico", "*.webp" },
                },
            },
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file is not null)
        {
            IconBox.Text = file.Path.LocalPath;
        }
    }

    private void OnConfirm(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_item is null)
        {
            Close();
            return;
        }

        _item.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "未命名项目" : NameBox.Text!;
        _item.Kind = IsCommand ? "command" : "app";
        _item.Command = IsCommand ? (CommandBox.Text ?? string.Empty) : string.Empty;
        _item.Path = IsCommand ? string.Empty : (PathBox.Text ?? string.Empty);
        _item.Icon = IconBox.Text;
        _item.Hotkey = HotkeyBox.Text ?? string.Empty;
        _item.IsEnabled = EnabledBox.IsChecked == true;
        _confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _confirmed = false;
        Close();
    }
}
