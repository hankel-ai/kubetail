using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KubeTail.Models;
using KubeTail.Services;
using System.Collections.ObjectModel;

namespace KubeTail.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ConfigService _config = new();

    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    [ObservableProperty] private TabViewModel? _selectedTab;
    [ObservableProperty] private string? _currentProfileName;
    public ObservableCollection<ClusterInfo> Clusters { get; } = new();
    public ObservableCollection<SavedTabProfile> SavedProfiles { get; } = new();

    private List<SmartLogDefinition> _smartLogDefs = new();

    public MainViewModel()
    {
        LoadClusters();
        LoadProfiles();
        _smartLogDefs = _config.GetSmartLogDefinitions();
        var lastProfile = _config.Load().LastProfileName;
        if (!string.IsNullOrEmpty(lastProfile))
            CurrentProfileName = lastProfile;
        if (Tabs.Count == 0) AddTab();
    }

    private void LoadClusters()
    {
        Clusters.Clear();
        var saved = _config.Load().SavedClusters;
        var fromKube = KubeService.ReadContexts();

        // Merge: keep saved descriptions, add new contexts
        foreach (var ctx in fromKube)
        {
            var existing = saved.FirstOrDefault(s => s.ContextName == ctx.ContextName);
            Clusters.Add(existing ?? ctx);
        }
    }

    private void LoadProfiles()
    {
        SavedProfiles.Clear();
        foreach (var p in _config.Load().SavedTabProfiles)
            SavedProfiles.Add(p);
    }

    [RelayCommand]
    private void AddTab()
    {
        var tab = new TabViewModel { Name = $"Tab {Tabs.Count + 1}" };
        tab.SetSmartLogDefinitions(_smartLogDefs);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void CloseTab(TabViewModel? tab)
    {
        if (tab == null) return;
        tab.StopStreamingCommand.Execute(null);
        tab.Dispose();
        Tabs.Remove(tab);
        if (Tabs.Count == 0) AddTab();
        SelectedTab = Tabs.LastOrDefault();
    }

    [RelayCommand]
    private void SaveProfile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _config.SaveTabProfile(name, Tabs.Select(t => t.ToConfig()).ToList());
        CurrentProfileName = name;
        PersistLastProfileName(name);
        LoadProfiles();
    }

    [RelayCommand]
    private void LoadProfile(SavedTabProfile? profile)
    {
        if (profile == null) return;

        // Stop and dispose all existing tabs without triggering auto-add
        foreach (var t in Tabs.ToList())
        {
            t.StopStreamingCommand.Execute(null);
            t.Dispose();
        }
        Tabs.Clear();

        foreach (var cfg in profile.Tabs)
        {
            var tab = new TabViewModel();
            tab.SetSmartLogDefinitions(_smartLogDefs);
            tab.LoadConfig(cfg);
            Tabs.Add(tab);

            // Connect to the cluster and auto-start streaming
            var clusterCtx = cfg.Sources.FirstOrDefault()?.ClusterContext;
            if (clusterCtx != null)
            {
                var cluster = Clusters.FirstOrDefault(c => c.ContextName == clusterCtx);
                if (cluster != null)
                {
                    tab.KubeService.Connect(cluster);
                    tab.CurrentCluster = cluster;
                    if (tab.Sources.Count > 0)
                        tab.StartStreamingCommand.Execute(null);
                }
            }
        }
        CurrentProfileName = profile.ProfileName;
        PersistLastProfileName(profile.ProfileName);
        SelectedTab = Tabs.FirstOrDefault();
    }

    [RelayCommand]
    private void DeleteProfile(SavedTabProfile? profile)
    {
        if (profile == null) return;
        var config = _config.Load();
        config.SavedTabProfiles.RemoveAll(p => p.ProfileName == profile.ProfileName);
        _config.Save(config);
        LoadProfiles();
    }

    private void PersistLastProfileName(string? name)
    {
        var c = _config.Load();
        c.LastProfileName = name;
        _config.Save(c);
    }

    public void SaveCluster(ClusterInfo cluster)
    {
        var existing = Clusters.FirstOrDefault(c => c.ContextName == cluster.ContextName);
        if (existing != null) Clusters.Remove(existing);
        Clusters.Add(cluster);
        _config.SaveClusters(Clusters.ToList());
    }

    public void SaveAllClusters() => _config.SaveClusters(Clusters.ToList());

    public void ReloadSmartLogDefinitions()
    {
        _smartLogDefs = _config.GetSmartLogDefinitions();
        foreach (var tab in Tabs)
            tab.SetSmartLogDefinitions(_smartLogDefs);
    }

    public List<SmartLogDefinition> SmartLogDefinitions => _smartLogDefs;
    public ConfigService ConfigService => _config;
}
