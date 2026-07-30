using System.Windows;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Views;

public partial class ImportPreviewWindow : Window
{
    private readonly ImportPreview _preview;
    private readonly Func<ImportPreview, ImportMergeResult> _commit;

    public ImportPreviewWindow(ImportPreview preview, Func<ImportPreview, ImportMergeResult> commit)
    {
        InitializeComponent();
        _preview = preview;
        _commit = commit;
        DataContext = preview;
    }

    public ImportMergeResult? Result { get; private set; }

    private void ConfirmImport_Click(object sender, RoutedEventArgs e)
    {
        if (!_preview.CanConfirm) return;

        ImportErrorText.Visibility = Visibility.Collapsed;
        ConfirmImportButton.IsHitTestVisible = false;
        try
        {
            Result = _commit(_preview);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ImportErrorText.Text = $"保存失败：{ex.Message}";
            ImportErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            ConfirmImportButton.IsHitTestVisible = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}