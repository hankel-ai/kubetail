using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace KubeTail.Models;

public partial class TreeNode : ObservableObject
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public TreeNode? Parent { get; set; }
    public ObservableCollection<TreeNode> Children { get; set; } = new();

    [ObservableProperty] private bool? _isChecked = false;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isCheckable = true;

    public string ControllerKind { get; set; } = "";
    public string ContainerName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string ClusterContext { get; set; } = "";

    private bool _suppress;

    partial void OnIsCheckedChanged(bool? value)
    {
        if (_suppress) return;
        if (value.HasValue)
        {
            _suppress = true;
            SetChildrenChecked(value.Value);
            _suppress = false;
        }
        UpdateParentCheck();
    }

    private void SetChildrenChecked(bool val)
    {
        foreach (var c in Children)
        {
            c._suppress = true;
            c.IsChecked = val;
            c.SetChildrenChecked(val);
            c._suppress = false;
        }
    }

    private void UpdateParentCheck()
    {
        if (Parent == null || _suppress) return;
        var all = Parent.Children.All(c => c.IsChecked == true);
        var none = Parent.Children.All(c => c.IsChecked == false);
        Parent._suppress = true;
        Parent.IsChecked = all ? true : none ? false : null;
        Parent._suppress = false;
        Parent.UpdateParentCheck();
    }

    public List<LogSource> GetSelectedSources()
    {
        var results = new List<LogSource>();
        CollectSources(this, results);
        return results;
    }

    private static void CollectSources(TreeNode node, List<LogSource> results)
    {
        if (node.Kind == "Container" && node.IsChecked == true)
        {
            var ctrl = node.Parent?.Parent; // Container -> Pod -> Controller
            if (ctrl != null)
            {
                results.Add(new LogSource
                {
                    ClusterContext = node.ClusterContext,
                    Namespace = node.Namespace,
                    ControllerKind = ctrl.ControllerKind,
                    ControllerName = ctrl.Name.Contains('/') ? ctrl.Name.Split('/')[1] : ctrl.Name,
                    ContainerName = node.ContainerName
                });
            }
        }
        foreach (var child in node.Children)
            CollectSources(child, results);
    }
}
