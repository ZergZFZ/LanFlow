using System.IO;
using System.Windows;


namespace LanFlow.Desktop.Views;

public partial class EditItemWindow : Window
{
    public EditItemWindow(string name, string path, string title)
    {
        InitializeComponent();
        Title = title;
        NameTextBox.Text = name;
        PathTextBox.Text = path;
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    public string ItemName => NameTextBox.Text.Trim();
    public string ItemPath => PathTextBox.Text.Trim();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择程序、快捷方式或文件",
            CheckFileExists = true,
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        PathTextBox.Text = picker.FileName;
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            NameTextBox.Text = Path.GetFileNameWithoutExtension(picker.FileName);
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ItemName))
        {
            System.Windows.MessageBox.Show("显示名称不能为空。", "无法保存", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            NameTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(ItemPath) || (!File.Exists(ItemPath) && !Directory.Exists(ItemPath)))
        {
            System.Windows.MessageBox.Show("请选择有效的文件或目录。", "无法保存", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            PathTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
