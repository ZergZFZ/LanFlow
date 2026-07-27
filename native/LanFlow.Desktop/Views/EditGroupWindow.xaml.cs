using System.Windows;

namespace LanFlow.Desktop.Views;

public partial class EditGroupWindow : Window
{
    public EditGroupWindow(string name, string title)
    {
        InitializeComponent();
        Title = title;
        NameTextBox.Text = name;
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    public string GroupName => NameTextBox.Text.Trim();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GroupName))
        {
            System.Windows.MessageBox.Show("分组名称不能为空。", "无法保存", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
