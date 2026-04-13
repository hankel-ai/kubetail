using System.Windows;

namespace KubeTail.Views;

public partial class InputDialog : Window
{
    public string InputText => InputBox.Text;

    public InputDialog(string title, string prompt, string? defaultValue = null)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        if (!string.IsNullOrEmpty(defaultValue))
            InputBox.Text = defaultValue;
        InputBox.Focus();
        InputBox.SelectAll();
    }

    private void OK_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
