using System;
using System.Windows;
using System.Windows.Input;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop.Views;

/// <summary>
/// 取字结果窗：文本可编辑，Enter 复制并关闭（由宿主随后关闭截图窗），Esc 返回标注。
/// </summary>
public partial class OcrResultWindow : Window
{
    public OcrResultWindow(string text)
    {
        InitializeComponent();
        ResultBox.Text = text;
        Loaded += (_, _) =>
        {
            ResultBox.Focus();
            // 光标定位到开头：用户第一眼应看到正文起始；此前跳到文末，长文本时首屏是空的。
            ResultBox.CaretIndex = 0;
            ResultBox.ScrollToHome();
        };
    }

    /// <summary>用户是否已复制（决定截图窗是否继续关闭）。</summary>
    public bool Copied { get; private set; }

    public string ResultText => ResultBox.Text;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try { DragMove(); } catch { /* 忽略窗口关闭等边界情况 */ }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (e.Key == Key.Enter && !ctrl)
        {
            e.Handled = true;
            CopyAndClose();
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyAndClose();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void CopyAndClose()
    {
        if (!ClipboardText.TrySet(ResultBox.Text))
        {
            MessageBox.Show(this, "写入剪贴板失败，请稍后重试。", "LanFlow", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Copied = true;
        Close();
    }
}
