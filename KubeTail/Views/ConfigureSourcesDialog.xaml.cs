using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KubeTail.Models;
using KubeTail.Services;
using KubeTail.ViewModels;

namespace KubeTail.Views;

public partial class ConfigureSourcesDialog : Window
{
    private readonly ConfigureSourcesViewModel _vm;

    public List<LogSource> SelectedSources { get; private set; } = new();
    public ClusterInfo? SelectedCluster => _vm.SelectedCluster;

    public ConfigureSourcesDialog(KubeService kube, ObservableCollection<ClusterInfo> clusters,
        List<LogSource>? existingSources = null)
    {
        InitializeComponent();
        _vm = new ConfigureSourcesViewModel(kube, clusters, existingSources);
        DataContext = _vm;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        SelectedSources = _vm.CollectCheckedSources();
        DialogResult = true;
    }

    private void ToggleExclusions(object sender, RoutedEventArgs e)
    {
        var dlg = new ExclusionsDialog(_vm.ExclusionText) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _vm.ExclusionText = dlg.ExclusionText;
            _vm.SaveExclusionsCommand.Execute(null);
        }
    }

    private void ItemName_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is CheckItem item)
        {
            item.Select();
            e.Handled = true;
        }
    }

    private void CheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox cb && cb.Tag is CheckItem item)
        {
            // Force the property to match the checkbox state
            var isChecked = cb.IsChecked == true;
            if (item.IsChecked != isChecked)
                item.IsChecked = isChecked;
        }
    }
}
