using System.IO;
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
        try
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

            // B1-2：选中文件后自动命名。名称框为空或仍是占位"新项目"时自动填充：
            // .desktop 取桌面入口 Name，其余取文件名（去扩展名）。
            if (string.IsNullOrWhiteSpace(NameBox.Text) || NameBox.Text == "新项目")
            {
                if (path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
                {
                    var (name, _, _) = ShellIconService.ParseDesktop(path);
                    NameBox.Text = string.IsNullOrWhiteSpace(name)
                        ? Path.GetFileNameWithoutExtension(path)
                        : name;
                }
                else
                {
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    NameBox.Text = string.IsNullOrWhiteSpace(fileName)
                        ? Path.GetFileName(path)
                        : fileName;
                }
            }
        }
        catch (System.Exception ex)
        {
            // UOS 上文件选择器门户(DBus portal)可能缺失，异常必须有日志而不是静默崩溃
            System.Console.WriteLine("[取证] 文件选择器(路径)打开失败: " + ex);
        }
    }

    private async void OnBrowseIcon(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
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
        catch (System.Exception ex)
        {
            System.Console.WriteLine("[取证] 文件选择器(图标)打开失败: " + ex);
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
