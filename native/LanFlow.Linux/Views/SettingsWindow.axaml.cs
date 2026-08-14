using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
        // 隧道阶段拦截（handledEventsToo），先于 TextBox 自身按键处理，保证组合键能被捕获
        HotkeyBox.AddHandler(InputElement.KeyDownEvent, OnHotkeyBoxKeyDown,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        InitializeState();
        // 滑块实时预览接线放在 InitializeState 之后：XAML 里不挂 ValueChanged，
        // 否则解析期 Minimum 会把默认 Value(0) 强转到下限、在其余具名控件尚未创建时触发
        // RefreshLayoutValues 的空引用（表现为「设置」按钮点了没反应、窗口弹不出）。
        CardWidthSlider.ValueChanged += OnLayoutSliderChanged;
        CardHeightSlider.ValueChanged += OnLayoutSliderChanged;
        IconSizeSlider.ValueChanged += OnLayoutSliderChanged;
        TextSizeSlider.ValueChanged += OnLayoutSliderChanged;
        ItemSpacingSlider.ValueChanged += OnLayoutSliderChanged;
        RowSpacingSlider.ValueChanged += OnLayoutSliderChanged;
        ContentPaddingSlider.ValueChanged += OnLayoutSliderChanged;
        // B3-1：默认选中第一个分类（外观与主题）
        CategoryList.SelectedIndex = 0;
        ShowPanel(0);

        // 第三轮取证件（缺陷板 v2 §3.2）：窗口打开 500ms 后 dump 关键控件尺寸
        Opened += (_, _) =>
        {
            var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                DumpLayoutForensics();
            };
            timer.Start();
        };
    }

    /// <summary>第三轮取证件：dump 行为区五开关与热键框。Bounds 为零即 D1 未修复的直接证据。</summary>
    private void DumpLayoutForensics()
    {
        try
        {
            Console.WriteLine($"[取证] SettingsWindow Bounds={Bounds}");
            foreach (var control in new Control?[]
            {
                OpenSingleClickToggle, ShowBadgeToggle, ShowFullToggle, ShowTitleToggle, StartupToggle, HotkeyBox,
            })
            {
                if (control is null)
                {
                    Console.WriteLine("[取证] SettingsWindow: 控件引用为 null");
                    continue;
                }

                Console.WriteLine($"[取证] SettingsWindow {control.Name} type={control.GetType().Name} Bounds={control.Bounds} DesiredSize={control.DesiredSize} IsVisible={control.IsVisible}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[取证] SettingsWindow dump 失败: " + ex);
        }
    }

    public Action? OnApplied { get; set; }

    private void InitializeState()
    {
        _working = _viewModel.Settings.Clone();

        ProfileBox.ItemsSource = BuildProfileList();
        ProfileBox.SelectedItem = _working.ThemeProfile;
        ThemeProfileBox.Text = _working.ThemeProfile;

        OpacityBox.Value = _working.Opacity;
        TransparencyModeBox.SelectedIndex = _working.TransparencyMode == "wholeWindow" ? 1 : 0;
        RefreshOpacityFromMode();
        UpdateTransparencyHint();
        LayoutBox.SelectedIndex = _working.LayoutMode == "card" ? 1 : 0;
        GroupBoxLayoutBox.SelectedIndex = _working.GroupLayout == "top" ? 1 : 0;
        IconSizeSlider.Value = _working.IconSize;
        TextSizeSlider.Value = _working.TextSize;
        CardWidthSlider.Value = _working.CardWidth;
        CardHeightSlider.Value = _working.CardHeight;
        ItemSpacingSlider.Value = _working.ItemSpacing;
        RowSpacingSlider.Value = _working.RowSpacing;
        ContentPaddingSlider.Value = _working.ContentPadding;
        RefreshLayoutValues();
        RefreshPreview();
        ThemeProfileBox.Text = _working.ThemeProfile;

        OpenSingleClickToggle.IsChecked = _working.OpenItemsOnSingleClick;
        ShowBadgeToggle.IsChecked = _working.ShowShortcutBadge;
        ShowFullToggle.IsChecked = _working.ShowFullItemName;
        ShowTitleToggle.IsChecked = _working.ShowItemTitle;
        StartupToggle.IsChecked = _working.StartWithWindows;
        HideOnDeactivateToggle.IsChecked = _working.HideOnDeactivate;
        GroupHoverToggle.IsChecked = _working.GroupSwitchMode == "hover";
        GroupHoverDelayBox.Value = _working.GroupHoverDelayMs;
        AnimationToggle.IsChecked = _working.AnimationMode != "off";

        HotkeyBox.Text = _working.Hotkey;
        HotkeyHint.Text = "当前：" + _working.Hotkey;

        // B3-6 性能页 / B3-5 关于页
        CacheStatusText.Text = string.Empty;
        ConfigPathText.Text = Path.Combine(ConfigDir, "config.json");
        VersionText.Text = "版本：" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "未知");

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

        // D17 根因修复：切换主题必须同步"主题配置名称"文本框，否则 OnConfirm 会用
        // 残留的旧名称（默认"深色"）覆盖 ThemeProfile，导致下次进入设置时主题回退深色。
        ThemeProfileBox.Text = selected;
        UpdateColorButtons();
    }

    private void OnLayoutChanged(object? sender, SelectionChangedEventArgs e) => RefreshPreview();

    // ---- 布局实时预览：滑块改动即时重绘样张 + 刷新数值 ----
    private void OnLayoutSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        RefreshLayoutValues();
        RefreshPreview();
    }

    private void RefreshLayoutValues()
    {
        CardWidthValue.Text = $"{(int)CardWidthSlider.Value}";
        CardHeightValue.Text = $"{(int)CardHeightSlider.Value}";
        IconSizeValue.Text = $"{(int)IconSizeSlider.Value}";
        TextSizeValue.Text = $"{(int)TextSizeSlider.Value}";
        ItemSpacingValue.Text = $"{(int)ItemSpacingSlider.Value}";
        RowSpacingValue.Text = $"{(int)RowSpacingSlider.Value}";
        ContentPaddingValue.Text = $"{(int)ContentPaddingSlider.Value}";
    }

    private void RefreshPreview()
    {
        var cell = LayoutBox.SelectedIndex == 0 ? BuildPreviewTile() : BuildPreviewCard();
        cell.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        cell.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        PreviewHost.Children.Clear();
        PreviewHost.Children.Add(cell);
    }

    private Border BuildPreviewTile()
    {
        var icon = BuildPreviewIcon();
        var title = new TextBlock
        {
            Text = "示例项目",
            FontSize = TextSizeSlider.Value,
            Foreground = GetThemeBrush("TextPrimaryBrush"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0),
            MaxWidth = CardWidthSlider.Value,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var stack = new StackPanel
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        stack.Children.Add(icon);
        stack.Children.Add(title);
        return new Border { Width = CardWidthSlider.Value, Height = CardHeightSlider.Value, Child = stack };
    }

    private Border BuildPreviewCard()
    {
        var icon = BuildPreviewIcon();
        var title = new TextBlock
        {
            Text = "示例项目",
            FontSize = TextSizeSlider.Value,
            Foreground = GetThemeBrush("TextPrimaryBrush"),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = Math.Max(0, CardWidthSlider.Value - IconSizeSlider.Value - 24),
        };
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        stack.Children.Add(icon);
        stack.Children.Add(title);
        return new Border
        {
            Width = CardWidthSlider.Value,
            Height = CardHeightSlider.Value,
            Padding = new Thickness(10),
            Background = GetThemeBrush("SurfaceBrush"),
            BorderBrush = GetThemeBrush("SurfaceBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = stack,
        };
    }

    private Border BuildPreviewIcon()
    {
        var letter = new TextBlock
        {
            Text = "示",
            Foreground = GetThemeBrush("TextSecondaryBrush"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        return new Border
        {
            Width = IconSizeSlider.Value,
            Height = IconSizeSlider.Value,
            CornerRadius = new CornerRadius(10),
            Background = GetThemeBrush("IconSurfaceBrush"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Child = letter,
        };
    }

    private IBrush GetThemeBrush(string key) =>
        Application.Current?.Resources[key] as IBrush ?? Brushes.Gray;

    /// <summary>热键框"按键即录入"：按住修饰键再按值键直接生成组合键文本，免手动输入。</summary>
    private void OnHotkeyBoxKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        // 纯修饰键：拦截但不录入，等待真正的值键（物理键判断，X11 下更可靠）
        if (e.PhysicalKey is PhysicalKey.ControlLeft or PhysicalKey.ControlRight
            or PhysicalKey.AltLeft or PhysicalKey.AltRight
            or PhysicalKey.ShiftLeft or PhysicalKey.ShiftRight
            or PhysicalKey.MetaLeft or PhysicalKey.MetaRight)
        {
            e.Handled = true;
            return;
        }

        var mods = e.KeyModifiers;
        if (mods == KeyModifiers.None)
        {
            return; // 无修饰键时放行，保留手动编辑
        }

        // 符号键优先：按住 Shift 的标点/数字排按键用物理键 + Shift 映射出实际符号字符
        // （如 | ? ~ _ ! @ 等），符号本身已隐含 Shift，展示时不重复加 "Shift"。
        var shifted = mods.HasFlag(KeyModifiers.Shift);
        var symbol = SymbolFromPhysicalKey(e.PhysicalKey, shifted);

        // 物理键优先（X11 组合键的逻辑 keysym 会被修饰键污染），逻辑键兜底
        string? token;
        if (symbol is not null)
        {
            token = symbol;
        }
        else
        {
            token = TokenFromPhysicalKey(e.PhysicalKey) ?? TokenFromKey(e.Key);
        }

        if (token is null)
        {
            return;
        }

        var parts = new List<string>();
        if (mods.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (symbol is null && mods.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(token);

        HotkeyBox.Text = string.Join("+", parts);
        HotkeyBox.CaretIndex = HotkeyBox.Text.Length;
        e.Handled = true;
    }

    private static string? TokenFromPhysicalKey(PhysicalKey pk)
    {
        var s = pk.ToString();
        if (s.Length == 1 && s[0] >= 'A' && s[0] <= 'Z') return s;                      // 字母
        if (s.StartsWith("Digit") && s.Length == 6) return s.Substring(5);              // 主键盘数字
        if (s.Length > 1 && s[0] == 'F' && int.TryParse(s.Substring(1), out _)) return s; // F 键
        return pk switch
        {
            PhysicalKey.Space => "Space",
            PhysicalKey.Enter => "Enter",
            PhysicalKey.Escape => "Esc",
            PhysicalKey.Backspace => "Backspace",
            PhysicalKey.Delete => "Delete",
            PhysicalKey.Insert => "Insert",
            PhysicalKey.Home => "Home",
            PhysicalKey.End => "End",
            PhysicalKey.PageUp => "PageUp",
            PhysicalKey.PageDown => "PageDown",
            PhysicalKey.ArrowUp => "Up",
            PhysicalKey.ArrowDown => "Down",
            PhysicalKey.ArrowLeft => "Left",
            PhysicalKey.ArrowRight => "Right",
            _ => null,
        };
    }

    /// <summary>把物理键 + Shift 状态映射为实际符号字符（美式布局）。符号本身隐含 Shift。</summary>
    private static string? SymbolFromPhysicalKey(PhysicalKey pk, bool shifted)
    {
        var n = pk.ToString();
        if (n.Length == 6 && n.StartsWith("Digit") && int.TryParse(n.Substring(5), out var d))
        {
            if (!shifted)
            {
                return null; // 无 Shift 的数字由 TokenFromPhysicalKey 输出 "0".."9"
            }

            // 美式键盘主数字排上档符号：1! 2@ 3# 4$ 5% 6^ 7& 8* 9( 0)
            const string row = "!@#$%^&*()";
            return row[d == 0 ? 9 : d - 1].ToString();
        }

        return pk switch
        {
            PhysicalKey.Backslash => shifted ? "|" : "\\",
            PhysicalKey.Slash => shifted ? "?" : "/",
            PhysicalKey.Backquote => shifted ? "~" : "`",
            PhysicalKey.Minus => shifted ? "_" : "-",
            PhysicalKey.Equal => shifted ? "+" : "=",
            PhysicalKey.BracketLeft => shifted ? "{" : "[",
            PhysicalKey.BracketRight => shifted ? "}" : "]",
            PhysicalKey.Semicolon => shifted ? ":" : ";",
            PhysicalKey.Quote => shifted ? "\"" : "'",
            PhysicalKey.Comma => shifted ? "<" : ",",
            PhysicalKey.Period => shifted ? ">" : ".",
            _ => null,
        };
    }

    private static string? TokenFromKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Space => "Space",
        Key.Enter or Key.Return => "Enter",
        Key.Escape => "Esc",
        Key.Back => "Backspace",
        Key.Delete => "Delete",
        Key.Insert => "Insert",
        Key.Home => "Home",
        Key.End => "End",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        Key.Up or Key.Down or Key.Left or Key.Right => key.ToString(),
        _ => null,
    };

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
            HotkeyHint.Text = "格式无效，示例：Ctrl+Alt+L / Ctrl+Shift+A";
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
        _working.TransparencyMode = TransparencyModeBox.SelectedIndex == 1 ? "wholeWindow" : "layered";
        if (_working.TransparencyMode == "wholeWindow")
        {
            _working.WholeWindowOpacity = OpacityBox.Value;
        }
        else
        {
            _working.LayeredOpacity = OpacityBox.Value;
        }
        _working.LayoutMode = LayoutBox.SelectedIndex == 1 ? "card" : "tile";
        _working.GroupLayout = GroupBoxLayoutBox.SelectedIndex == 1 ? "top" : "left";
        _working.IconSize = IconSizeSlider.Value;
        _working.TextSize = TextSizeSlider.Value;
        _working.CardWidth = CardWidthSlider.Value;
        _working.CardHeight = CardHeightSlider.Value;
        _working.ItemSpacing = ItemSpacingSlider.Value;
        _working.RowSpacing = RowSpacingSlider.Value;
        _working.ContentPadding = ContentPaddingSlider.Value;
        _working.OpenItemsOnSingleClick = OpenSingleClickToggle.IsChecked == true;
        _working.ShowShortcutBadge = ShowBadgeToggle.IsChecked == true;
        _working.ShowFullItemName = ShowFullToggle.IsChecked == true;
        _working.ShowItemTitle = ShowTitleToggle.IsChecked == true;
        _working.StartWithWindows = StartupToggle.IsChecked == true;
        _working.HideOnDeactivate = HideOnDeactivateToggle.IsChecked == true;
        _working.GroupSwitchMode = GroupHoverToggle.IsChecked == true ? "hover" : "click";
        _working.GroupHoverDelayMs = (int)GroupHoverDelayBox.Value.GetValueOrDefault();
        _working.AnimationMode = AnimationToggle.IsChecked == false ? "off" : "on";

        if (HotkeyService.TryNormalize(HotkeyBox.Text ?? string.Empty, out var normalized))
        {
            _working.Hotkey = normalized;
        }

        if (ProfileBox.SelectedItem is string profile)
        {
            _working.ThemeProfile = profile;
        }

        if (!string.IsNullOrWhiteSpace(ThemeProfileBox.Text))
        {
            _working.ThemeProfile = ThemeProfileBox.Text!;
        }

        _viewModel.ApplyAppearance(_working, persist: true);
        App.ApplyThemeColors(_viewModel.Settings);
        _startup.SetEnabled(_working.StartWithWindows);
        OnApplied?.Invoke();
        Close();
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    // ---- B3-1 分类导航 ----
    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedIndex >= 0)
        {
            ShowPanel(CategoryList.SelectedIndex);
        }
    }

    private void ShowPanel(int index)
    {
        PanelAppearance.IsVisible = index == 0;
        PanelLayout.IsVisible = index == 1;
        PanelGroups.IsVisible = index == 2;
        PanelTransparency.IsVisible = index == 3;
        PanelInteraction.IsVisible = index == 4;
        PanelStartup.IsVisible = index == 5;
        PanelPerformance.IsVisible = index == 6;
        PanelAbout.IsVisible = index == 7;
    }

    // ---- B3-4 主题配置命名 ----
    private void OnThemeProfileChanged(object? sender, TextChangedEventArgs e) =>
        _working.ThemeProfile = string.IsNullOrWhiteSpace(ThemeProfileBox.Text)
            ? "自定义风格"
            : ThemeProfileBox.Text!;

    // ---- B3-2 透明度双模式 ----
    private void OnTransparencyModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshOpacityFromMode();
        UpdateTransparencyHint();
    }

    private void RefreshOpacityFromMode()
    {
        if (TransparencyModeBox.SelectedIndex == 1)
        {
            OpacityBox.Value = _working.WholeWindowOpacity;
        }
        else
        {
            OpacityBox.Value = _working.LayeredOpacity;
        }

        OpacityValueText.Text = $"{(int)Math.Round(Math.Clamp(OpacityBox.Value, 0.55, 1.0) * 100)}%";
    }

    /// <summary>滑块拖动实时显示百分比，让透明度调节有直接反馈。</summary>
    private void OnOpacityValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var value = OpacityBox.Value;
        OpacityValueText.Text = $"{(int)Math.Round(Math.Clamp(value, 0.55, 1.0) * 100)}%";
    }

    /// <summary>透明模式说明，避免用户误以为滑块"失效"。</summary>
    private void UpdateTransparencyHint()
    {
        TransparencyHint.Text = TransparencyModeBox.SelectedIndex == 1
            ? "整窗透明：整个窗口（含背景）半透明，可透出桌面与窗口管理器合成效果。"
            : "分层透明：仅主界面项目区域内容半透明，顶部搜索栏、底部按钮栏与分组栏保持不透明。";
    }

    private void OnResetOpacity(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpacityBox.Value = 0.85;
        if (TransparencyModeBox.SelectedIndex == 1)
        {
            _working.WholeWindowOpacity = 0.85;
        }
        else
        {
            _working.LayeredOpacity = 0.85;
        }
    }

    // ---- B3-6 性能页 ----
    // 复用 ConfigStore 统一解析（含 LANFLOW_CONFIG_DIR 覆盖），避免与运行目录不一致。
    private static string ConfigDir => ConfigStore.ResolveConfigDirectory();

    private void OnClearIconCache(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShellIconService.Clear();
        CacheStatusText.Text = "已清空图标缓存，下次显示时按需重新加载";
    }

    private void OnOpenConfigPath(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }

            Process.Start(new ProcessStartInfo("xdg-open", ConfigDir) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            System.Console.WriteLine("[取证] 打开配置目录失败: " + ex);
        }
    }
}
