using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KubeTail.Models;
using KubeTail.Services;
using System.Collections.ObjectModel;

namespace KubeTail.ViewModels;

public partial class ConfigureSourcesViewModel : ObservableObject
{
    private readonly KubeService _kube;
    private readonly ConfigService _configService = new();

    public ObservableCollection<ClusterInfo> Clusters { get; }
    [ObservableProperty] private ClusterInfo? _selectedCluster;

    public ObservableCollection<CheckItem> Namespaces { get; } = new();
    public ObservableCollection<CheckItem> Controllers { get; } = new();
    public ObservableCollection<CheckItem> Pods { get; } = new();
    public ObservableCollection<CheckItem> Containers { get; } = new();

    public string ExclusionText { get; set; }
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Select a cluster.";

    private List<string> _exclusions;
    private List<LogSource> _existingSources = new();

    private readonly Dictionary<CheckItem, string> _ctrlNs = new();
    private readonly Dictionary<CheckItem, (string Kind, string Name, string Ns)> _ctrlMeta = new();
    private readonly Dictionary<CheckItem, (CheckItem Ctrl, string Ns)> _podMeta = new();
    private readonly Dictionary<CheckItem, (CheckItem Pod, string Ns)> _containerMeta = new();

    private readonly HashSet<CheckItem> _previewControllers = new();
    private readonly HashSet<CheckItem> _previewPods = new();
    private readonly HashSet<CheckItem> _previewContainers = new();

    public ConfigureSourcesViewModel(KubeService kube, ObservableCollection<ClusterInfo> clusters,
        List<LogSource>? existingSources = null)
    {
        _kube = kube;
        Clusters = clusters;
        _existingSources = existingSources ?? new();
        _exclusions = _configService.GetNamespaceExclusions();
        ExclusionText = string.Join("\n", _exclusions);

        // Auto-select cluster from existing sources or current context
        var clusterCtx = _existingSources.FirstOrDefault()?.ClusterContext
            ?? KubeService.GetCurrentContext();
        if (clusterCtx != null)
            SelectedCluster = Clusters.FirstOrDefault(c => c.ContextName == clusterCtx);
    }

    partial void OnSelectedClusterChanged(ClusterInfo? value)
    {
        ClearAll();
        if (value != null) _ = LoadNamespacesAndRestoreAsync(value);
    }

    private void ClearAll()
    {
        Namespaces.Clear(); Controllers.Clear(); Pods.Clear(); Containers.Clear();
        _ctrlNs.Clear(); _ctrlMeta.Clear(); _podMeta.Clear(); _containerMeta.Clear();
        _previewControllers.Clear(); _previewPods.Clear(); _previewContainers.Clear();
    }

    // ========== LOAD + RESTORE ==========
    private async Task LoadNamespacesAndRestoreAsync(ClusterInfo cluster)
    {
        IsLoading = true;
        StatusText = $"Connecting to {cluster.DisplayName}...";
        try
        {
            _kube.Connect(cluster);
            _exclusions = ExclusionText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var ns = await _kube.GetNamespacesAsync();
            Namespaces.Clear();
            foreach (var n in ns)
            {
                if (WildcardMatcher.IsExcluded(n, _exclusions)) continue;
                Namespaces.Add(new CheckItem(n, n, OnNsChecked, OnNsSelected));
            }
            StatusText = $"{Namespaces.Count} namespaces.";

            // Restore previously checked items
            if (_existingSources.Count > 0 && cluster.ContextName == (_existingSources.FirstOrDefault()?.ClusterContext ?? ""))
                await RestoreSourcesAsync();
        }
        catch (Exception ex) { StatusText = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private async Task RestoreSourcesAsync()
    {
        var byNs = _existingSources.GroupBy(s => s.Namespace);
        foreach (var nsGroup in byNs)
        {
            // Check the namespace item
            var nsItem = Namespaces.FirstOrDefault(n => n.Key == nsGroup.Key);
            if (nsItem != null) nsItem.SetCheckedSilent(true);

            var ctrls = await _kube.GetControllersAsync(nsGroup.Key);
            foreach (var c in ctrls)
            {
                var key = $"{nsGroup.Key}/{c.Kind}/{c.Name}";
                var matchingSources = nsGroup.Where(s => s.ControllerKind == c.Kind && s.ControllerName == c.Name).ToList();
                if (matchingSources.Count == 0) continue;

                // Add controller checked
                var ctrlItem = new CheckItem(key, $"[{nsGroup.Key}] {c.Display}", OnCtrlChecked, OnCtrlSelected) { Tag = c };
                _ctrlNs[ctrlItem] = nsGroup.Key;
                _ctrlMeta[ctrlItem] = (c.Kind, c.Name, nsGroup.Key);
                ctrlItem.SetCheckedSilent(true);
                Controllers.Add(ctrlItem);

                // Load pods
                var pods = await _kube.GetPodsForControllerDetailAsync(nsGroup.Key, c.Kind, c.Name);
                foreach (var p in pods)
                {
                    var podKey = $"{nsGroup.Key}/{p.Name}";
                    var podItem = new CheckItem(podKey, p.Display, OnPodChecked, OnPodSelected);
                    _podMeta[podItem] = (ctrlItem, nsGroup.Key);
                    podItem.SetCheckedSilent(true);
                    Pods.Add(podItem);

                    // Load containers
                    var containers = await _kube.GetContainersForPodAsync(nsGroup.Key, p.Name);
                    foreach (var cn in containers)
                    {
                        var cKey = $"{nsGroup.Key}/{p.Name}/{cn}";
                        var shouldCheck = matchingSources.Any(s => s.ContainerName == cn || s.ContainerName == "*");
                        var ci = new CheckItem(cKey, $"[{p.Name}] {cn}", null, null);
                        _containerMeta[ci] = (podItem, nsGroup.Key);
                        if (shouldCheck) ci.SetCheckedSilent(true);
                        Containers.Add(ci);
                    }
                }
            }
        }
    }

    // ========== NAMESPACE ==========
    private void OnNsSelected(CheckItem item)
    {
        RemovePreview(_previewControllers, Controllers);
        RemovePreview(_previewPods, Pods);
        RemovePreview(_previewContainers, Containers);
        _ = BrowseControllersAsync(item.Key);
    }

    private void OnNsChecked(CheckItem item)
    {
        if (item.IsChecked)
            _ = LoadAndCheckControllersAsync(item.Key);
        else
            UncheckControllersForNs(item.Key);
    }

    // ========== CONTROLLER ==========
    private async Task BrowseControllersAsync(string ns)
    {
        IsLoading = true;
        try
        {
            var ctrls = await _kube.GetControllersAsync(ns);
            foreach (var c in ctrls)
            {
                var key = $"{ns}/{c.Kind}/{c.Name}";
                if (Controllers.Any(x => x.Key == key)) continue;
                var item = new CheckItem(key, $"[{ns}] {c.Display}", OnCtrlChecked, OnCtrlSelected) { Tag = c };
                _ctrlNs[item] = ns;
                _ctrlMeta[item] = (c.Kind, c.Name, ns);
                _previewControllers.Add(item);
                Controllers.Add(item);
            }
        }
        catch (Exception ex) { StatusText = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private async Task LoadAndCheckControllersAsync(string ns)
    {
        IsLoading = true;
        try
        {
            var ctrls = await _kube.GetControllersAsync(ns);
            foreach (var c in ctrls)
            {
                var key = $"{ns}/{c.Kind}/{c.Name}";
                var existing = Controllers.FirstOrDefault(x => x.Key == key);
                if (existing != null)
                {
                    if (!existing.IsChecked) existing.SetCheckedSilent(true);
                    _previewControllers.Remove(existing);
                }
                else
                {
                    var item = new CheckItem(key, $"[{ns}] {c.Display}", OnCtrlChecked, OnCtrlSelected) { Tag = c };
                    _ctrlNs[item] = ns;
                    _ctrlMeta[item] = (c.Kind, c.Name, ns);
                    item.SetCheckedSilent(true);
                    Controllers.Add(item);
                }
                await LoadAndCheckPodsAsync(key, c.Kind, c.Name, ns);
            }
        }
        catch (Exception ex) { StatusText = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private void UncheckControllersForNs(string ns)
    {
        var items = Controllers.Where(c => _ctrlNs.ContainsKey(c) && _ctrlNs[c] == ns).ToList();
        foreach (var c in items)
        {
            c.SetCheckedSilent(false);
            UncheckPodsForCtrl(c);
            Controllers.Remove(c);
            _ctrlNs.Remove(c); _ctrlMeta.Remove(c); _previewControllers.Remove(c);
        }
    }

    private void OnCtrlSelected(CheckItem item)
    {
        RemovePreview(_previewPods, Pods);
        RemovePreview(_previewContainers, Containers);
        if (_ctrlMeta.TryGetValue(item, out var m))
            _ = BrowsePodsAsync(item, m.Kind, m.Name, m.Ns);
    }

    private void OnCtrlChecked(CheckItem item)
    {
        if (!_ctrlMeta.TryGetValue(item, out var m)) return;
        if (item.IsChecked)
            _ = LoadAndCheckPodsAsync(item.Key, m.Kind, m.Name, m.Ns);
        else
            UncheckPodsForCtrl(item);
    }

    // ========== POD ==========
    private async Task BrowsePodsAsync(CheckItem ctrlItem, string kind, string name, string ns)
    {
        IsLoading = true;
        try
        {
            var pods = await _kube.GetPodsForControllerDetailAsync(ns, kind, name);
            foreach (var p in pods)
            {
                var key = $"{ns}/{p.Name}";
                if (Pods.Any(x => x.Key == key)) continue;
                var item = new CheckItem(key, p.Display, OnPodChecked, OnPodSelected);
                _podMeta[item] = (ctrlItem, ns);
                _previewPods.Add(item);
                Pods.Add(item);
            }
        }
        catch (Exception ex) { StatusText = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private async Task LoadAndCheckPodsAsync(string ctrlKey, string kind, string name, string ns)
    {
        var ctrlItem = Controllers.FirstOrDefault(c => c.Key == ctrlKey);
        if (ctrlItem == null) return;
        try
        {
            var pods = await _kube.GetPodsForControllerDetailAsync(ns, kind, name);
            foreach (var p in pods)
            {
                var key = $"{ns}/{p.Name}";
                var existing = Pods.FirstOrDefault(x => x.Key == key);
                if (existing != null)
                {
                    if (!existing.IsChecked) existing.SetCheckedSilent(true);
                    _previewPods.Remove(existing);
                }
                else
                {
                    var item = new CheckItem(key, p.Display, OnPodChecked, OnPodSelected);
                    _podMeta[item] = (ctrlItem, ns);
                    item.SetCheckedSilent(true);
                    Pods.Add(item);
                }
                await LoadAndCheckContainersAsync(key, p.Name, ns);
            }
        }
        catch { }
    }

    private void UncheckPodsForCtrl(CheckItem ctrlItem)
    {
        var items = Pods.Where(p => _podMeta.ContainsKey(p) && _podMeta[p].Ctrl == ctrlItem).ToList();
        foreach (var p in items)
        {
            UncheckContainersForPod(p);
            Pods.Remove(p); _podMeta.Remove(p); _previewPods.Remove(p);
        }
    }

    private void OnPodSelected(CheckItem item)
    {
        RemovePreview(_previewContainers, Containers);
        if (_podMeta.TryGetValue(item, out var m))
        {
            var podName = item.Key.Split('/').Last();
            _ = BrowseContainersAsync(item, podName, m.Ns);
        }
    }

    private void OnPodChecked(CheckItem item)
    {
        if (!_podMeta.TryGetValue(item, out var m)) return;
        var podName = item.Key.Split('/').Last();
        if (item.IsChecked)
            _ = LoadAndCheckContainersAsync(item.Key, podName, m.Ns);
        else
            UncheckContainersForPod(item);
    }

    // ========== CONTAINER ==========
    private async Task BrowseContainersAsync(CheckItem podItem, string podName, string ns)
    {
        IsLoading = true;
        try
        {
            var containers = await _kube.GetContainersForPodAsync(ns, podName);
            foreach (var c in containers)
            {
                var key = $"{ns}/{podName}/{c}";
                if (Containers.Any(x => x.Key == key)) continue;
                var item = new CheckItem(key, $"[{podName}] {c}", null, null);
                _containerMeta[item] = (podItem, ns);
                _previewContainers.Add(item);
                Containers.Add(item);
            }
        }
        catch (Exception ex) { StatusText = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private async Task LoadAndCheckContainersAsync(string podKey, string podName, string ns)
    {
        var podItem = Pods.FirstOrDefault(p => p.Key == podKey);
        if (podItem == null) return;
        try
        {
            var containers = await _kube.GetContainersForPodAsync(ns, podName);
            foreach (var c in containers)
            {
                var key = $"{ns}/{podName}/{c}";
                var existing = Containers.FirstOrDefault(x => x.Key == key);
                if (existing != null)
                {
                    if (!existing.IsChecked) existing.SetCheckedSilent(true);
                    _previewContainers.Remove(existing);
                }
                else
                {
                    var item = new CheckItem(key, $"[{podName}] {c}", null, null);
                    _containerMeta[item] = (podItem, ns);
                    item.SetCheckedSilent(true);
                    Containers.Add(item);
                }
            }
        }
        catch { }
    }

    private void UncheckContainersForPod(CheckItem podItem)
    {
        var items = Containers.Where(c => _containerMeta.ContainsKey(c) && _containerMeta[c].Pod == podItem).ToList();
        foreach (var c in items)
        {
            Containers.Remove(c); _containerMeta.Remove(c); _previewContainers.Remove(c);
        }
    }

    // ========== HELPERS ==========
    private static void RemovePreview(HashSet<CheckItem> preview, ObservableCollection<CheckItem> col)
    {
        foreach (var item in preview.ToList())
        {
            if (!item.IsChecked) { col.Remove(item); preview.Remove(item); }
        }
    }

    /// <summary>Collect all checked containers into LogSource list. Called by OK.</summary>
    public List<LogSource> CollectCheckedSources()
    {
        var results = new List<LogSource>();
        if (SelectedCluster == null) return results;

        foreach (var ci in Containers.Where(c => c.IsChecked))
        {
            if (!_containerMeta.TryGetValue(ci, out var cm)) continue;
            if (!_podMeta.TryGetValue(cm.Pod, out var pm)) continue;
            if (!_ctrlMeta.TryGetValue(pm.Ctrl, out var ctrlM)) continue;
            var parts = ci.Key.Split('/');
            var containerName = parts.Length >= 3 ? parts[^1] : ci.Key;
            var src = new LogSource
            {
                ClusterContext = SelectedCluster.ContextName,
                Namespace = ctrlM.Ns,
                ControllerKind = ctrlM.Kind,
                ControllerName = ctrlM.Name,
                ContainerName = containerName
            };
            if (!results.Any(s => s.Key == src.Key))
                results.Add(src);
        }
        return results;
    }

    [RelayCommand]
    private void SaveExclusions()
    {
        _exclusions = ExclusionText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _configService.SaveNamespaceExclusions(_exclusions);
        if (SelectedCluster != null) _ = LoadNamespacesAndRestoreAsync(SelectedCluster);
    }
}

public partial class CheckItem : ObservableObject
{
    public string Key { get; }
    public string DisplayText { get; set; }
    public object? Tag { get; set; }
    private readonly Action<CheckItem>? _onChecked;
    private readonly Action<CheckItem>? _onSelected;
    private bool _suppress;

    [ObservableProperty] private bool _isChecked;

    public CheckItem(string key, string display, Action<CheckItem>? onChecked, Action<CheckItem>? onSelected)
    {
        Key = key;
        DisplayText = display;
        _onChecked = onChecked;
        _onSelected = onSelected;
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_suppress) _onChecked?.Invoke(this);
    }

    public void SetCheckedSilent(bool value)
    {
        _suppress = true;
        IsChecked = value;
        _suppress = false;
    }

    public void Select() => _onSelected?.Invoke(this);
}
