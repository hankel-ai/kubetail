using System.Windows;

namespace KubeTail.Views;

public partial class ExclusionsDialog : Window
{
    public string ExclusionText => ExclusionBox.Text;

    public ExclusionsDialog(string currentExclusions)
    {
        InitializeComponent();
        ExclusionBox.Text = currentExclusions;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
