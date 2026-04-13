using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using KubeTail.Models;
using KubeTail.Services;

namespace KubeTail.Views;

public partial class SmartLogConfigDialog : Window
{
    private readonly ConfigService _config;
    public ObservableCollection<SmartLogDefinition> Definitions { get; }

    public SmartLogConfigDialog(List<SmartLogDefinition> definitions, ConfigService config)
    {
        InitializeComponent();
        _config = config;
        Definitions = new ObservableCollection<SmartLogDefinition>(definitions);
        DefList.ItemsSource = Definitions;
    }

    private void AddDefinition(object sender, RoutedEventArgs e)
    {
        var desc = NewDescription.Text.Trim();
        var kind = NewControllerKind.Text.Trim();
        var name = NewControllerName.Text.Trim();
        var container = NewContainerName.Text.Trim();
        var path = NewLogFilePath.Text.Trim();

        if (string.IsNullOrEmpty(desc) || string.IsNullOrEmpty(path))
            return;

        Definitions.Add(new SmartLogDefinition
        {
            Description = desc,
            ControllerKind = string.IsNullOrEmpty(kind) ? "*" : kind,
            ControllerName = string.IsNullOrEmpty(name) ? "*" : name,
            ContainerName = string.IsNullOrEmpty(container) ? "*" : container,
            LogFilePath = path,
            IsBuiltIn = false
        });

        NewDescription.Clear();
        NewControllerKind.Text = "Deployment";
        NewControllerName.Clear();
        NewContainerName.Text = "*";
        NewLogFilePath.Clear();
    }

    private void RemoveDefinition(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SmartLogDefinition def)
            Definitions.Remove(def);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _config.SaveSmartLogDefinitions(Definitions.ToList());
        DialogResult = true;
    }
}
