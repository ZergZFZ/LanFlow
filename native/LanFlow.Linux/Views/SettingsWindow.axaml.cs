using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LanFlow.Desktop;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;
using LanFlow.Desktop.ViewModels;
using LanFlow.Linux;

namespace LanFlow.Desktop.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly StartupService _startup = new();
    private Settings _working = new();

    private readonly (string Label, Func<ThemeColors, string> Get, Action<ThemeColors, string> Set)[] _colorDefs =
    {
        ("面板背景", c => c.Panel, (c, v) => c.Panel = v),
        ("面板边框", c => c.PanelBorder, (c, v) => c.PanelBorder = v),
        ("表面背景", c => c.Surface, (c, v) => c.Surface = v),
        ("表面边框", c => c.SurfaceBorder, (c, v) => c.SurfaceBorder = v),
        ("底栏背景", c => c.Footer, (c, v) => c.Footer = v),
        ("主文本", c => c.TextPrimary, (c, v) => c.TextPrimary = v),
        ("次要文本", c => c.TextSecondary, (c, v) => c.TextSecondary = v),
        ("强调色", c => c.Accent, (c, v) => c.Accent = v),
        ("悬停背景", c => c.Hover, (c, v) => c.Hover = v),
        ("图标背景", c => c.IconSurface, (c, v) => c.IconSurface = v),
    };

    private readonly Dictionary<int, Border> _colorBorders = new();
    private ColorPickerWindow? _picker;

    public SettingsWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        // 关键：把窗口高度限制在屏幕工作区 90% 以内，避免底部（快捷键等）被裁掉且滚动条够不到
        try
        {
            var primary = Screens.Primary;
            if (primary != null)
            {
                MaxHeight = Math.Clamp((int)(primary.WorkingArea.Height * 0.9), 360, 900);
            }
        }
        catch
        {
            // 个别平台在构造期 Screens 不可用，忽略，靠固定 Height 兜底
        }

        LayoutBox.SelectionChanged += OnLayoutChanged;
        InitializeState();
    }

    public Action? OnApplied { get; set; }

    private void InitializeState()
    {
        _working = _viewModel.Settings.Clone();

        ProfileBox.ItemsSource = BuildProfileList();
        ProfileBox.SelectedItem = _working.ThemeProfile;

        OpacityBox.Value = _working.Opacity;
        LayoutBox.SelectedIndex = _working.LayoutMode == "card" ? 1 : 0;
        GroupBoxLayoutBox.SelectedIndex = _working.GroupLayout == "top" ? 1 : 0;
        IconSizeBox.Value = (decimal)_working.IconSize;
        TextSizeBox.Value = (decimal)_working.TextSize;
        CardSizeBox.Value = (decimal)_working.CardSize;
        ItemSpacingBox.Value = (decimal)_working.ItemSpacing;
        RowSpacingBox.Value = (decimal)_working.RowSpacing;
        ContentPaddingBox.Value = (decimal)_working.ContentPadding;
        CardSizeRow.IsVisible = _working.LayoutMode == "card";

        OpenSingleClickToggle.IsChecked = _working.OpenItemsOnSingleClick;
        ShowBadgeToggle.IsChecked = _working.ShowShortcutBadge;
        ShowFullToggle.IsChecked = _working.ShowFullItemName;
        ShowTitleToggle.IsChecked = _working.ShowItemTitle;
        StartupToggle.IsChecked = _working.StartWithWindows;

        HotkeyBox.Text = _working.Hotkey;
        HotkeyHint.Text = "当前：" + _working.Hotkey;

        BuildColorRows();
    }

    private List<string> BuildProfileList()
    {
        var list = new List<string> { "深色", "浅色" };
        list.AddRange(_working.CustomThemes.Select(t => t.Name));
        return list;
    }

    private void BuildColorRows()
    {
        ColorPanel.Children.Clear();
        _colorBorders.Clear();
        for (var i = 0; i < _colorDefs.Length; i++)
        {
            var index = i;
            var def = _colorDefs[i];

            var label = new TextBlock
            {
                Text = def.Label,
                Foreground = (SolidColorBrush)Application.Current!.Resources["TextPrimaryBrush"]!,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Width = 90,
            };

            var border = new Border
            {
                Width = 64,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                BorderBrush = (SolidColorBrush)Application.Current.Resources["SurfaceBorderBrush"]!,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            border.PointerPressed += (_, _) => OpenColor(index);
            _colorBorders[index] = border;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Margin = new Thickness(0, 0, 0, 0),
            };
            row.Children.Add(label);
            row.Children.Add(border);
            ColorPanel.Children.Add(row);
        }

        UpdateColorButtons();
    }

    private void UpdateColorButtons()
    {
        foreach (var (index, border) in _colorBorders)
        {
            var hex = _colorDefs[index].Get(_working.ThemeColors);
            try
            {
                border.Background = new SolidColorBrush(Color.Parse(hex));
            }
            catch
            {
                border.Background = new SolidColorBrush(Colors.Gray);
            }
        }
    }

    private async void OpenColor(int index)
    {
        var def = _colorDefs[index];
        _picker = new ColorPickerWindow();
        _picker.Initialize(def.Get(_working.ThemeColors));
        await _picker.ShowDialog(this);
        if (_picker.Confirmed)
        {
            def.Set(_working.ThemeColors, _picker.ResultColor);
            UpdateColorButtons();
        }

        _picker = null;
    }

    private void OnProfileChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = ProfileBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        if (selected == "深色")
        {
            _working.Theme = "dark";
            _working.ThemeProfile = "深色";
            _working.ThemeColors = ThemeColors.Dark();
        }
        else if (selected == "浅色")
        {
            _working.Theme = "light";
            _working.ThemeProfile = "浅色";
            _working.ThemeColors = ThemeColors.Light();
        }
        else
        {
            var custom = _working.CustomThemes.FirstOrDefault(t => t.Name == selected);
            if (custom is not null)
            {
                _working.ThemeProfile = custom.Name;
                _working.ThemeColors = custom.Colors.Clone();
            }
        }

        UpdateColorButtons();
    }

    private void OnLayoutChanged(object? sender, SelectionChangedEventArgs e) =>
        CardSizeRow.IsVisible = LayoutBox.SelectedIndex == 1;

    private void OnApplyHotkey(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var input = HotkeyBox.Text ?? string.Empty;
        if (HotkeyService.TryNormalize(input, out var normalized))
        {
            _working.Hotkey = normalized;
            HotkeyHint.Text = "已应用：" + normalized;
        }
        else
        {
            HotkeyHint.Text = "格式无效，示例：Ctrl+Alt+Space / Ctrl+Shift+A";
        }
    }

    private void OnSaveTheme(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var name = "自定义风格 " + (_working.CustomThemes.Count + 1);
        _working.CustomThemes.Add(new ThemeProfile { Name = name, Colors = _working.ThemeColors.Clone() });
        ProfileBox.ItemsSource = BuildProfileList();
        ProfileBox.SelectedItem = name;
        _working.ThemeProfile = name;
    }

    private void OnConfirm(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _working.Opacity = OpacityBox.Value;
        _working.LayoutMode = LayoutBox.SelectedIndex == 1 ? "card" : "tile";
        _working.GroupLayout = GroupBoxLayoutBox.SelectedIndex == 1 ? "top" : "left";
        _working.IconSize = (double)IconSizeBox.Value.GetValueOrDefault();
        _working.TextSize = (double)TextSizeBox.Value.GetValueOrDefault();
        _working.CardSize = (double)CardSizeBox.Value.GetValueOrDefault();
        _working.ItemSpacing = (double)ItemSpacingBox.Value.GetValueOrDefault();
        _working.RowSpacing = (double)RowSpacingBox.Value.GetValueOrDefault();
        _working.ContentPadding = (double)ContentPaddingBox.Value.GetValueOrDefault();
        _working.OpenItemsOnSingleClick = OpenSingleClickToggle.IsChecked == true;
        _working.ShowShortcutBadge = ShowBadgeToggle.IsChecked == true;
        _working.ShowFullItemName = ShowFullToggle.IsChecked == true;
        _working.ShowItemTitle = ShowTitleToggle.IsChecked == true;
        _working.StartWithWindows = StartupToggle.IsChecked == true;

        if (HotkeyService.TryNormalize(HotkeyBox.Text ?? string.Empty, out var normalized))
        {
            _working.Hotkey = normalized;
        }

        if (ProfileBox.SelectedItem is string profile)
        {
            _working.ThemeProfile = profile;
        }

        _viewModel.ApplyAppearance(_working, persist: true);
        App.ApplyThemeColors(_viewModel.Settings);
        _startup.SetEnabled(_working.StartWithWindows);
        OnApplied?.Invoke();
        Close();
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
