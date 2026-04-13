using System.Collections.ObjectModel;
using System.Windows;
using KubeTail.Models;
using KubeTail.Services;

namespace KubeTail.Views;

public partial class ClusterManagerDialog : Window
{
    private readonly ObservableCollection<ClusterInfo> _clusters;

    public ClusterManagerDialog(ObservableCollection<ClusterInfo> clusters)
    {
        InitializeComponent();
        _clusters = clusters;
        Grid.ItemsSource = _clusters;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var fromKube = KubeService.ReadContexts();
        foreach (var ctx in fromKube)
        {
            if (!_clusters.Any(c => c.ContextName == ctx.ContextName))
                _clusters.Add(ctx);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
