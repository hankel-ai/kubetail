using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KubeTail.Models;
using KubeTail.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace KubeTail.ViewModels;

public partial class FilterItem : ObservableObject
{
    public string Name { get; set; } = "";
    [ObservableProperty] private bool _isChecked = true;
    private readonly Action? _onChanged;
    private bool _suppress;

    public FilterItem(string name, Action? onChanged)
    {
        Name = name; _onChanged = onChanged;
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_suppress) _onChanged?.Invoke();
    }

    public void SetSilent(bool val) { _suppress = true; IsChecked = val; _suppress = false; }
}

public partial class SmartLogPodItem : ObservableObject
{
    public string Pod { get; set; } = "";
    public SmartLogDefinition Definition { get; set; } = new();
    public LogSource Source { get; set; } = new();
    public string DisplayText => Pod;
    [ObservableProperty] private bool _isEnabled;
    private readonly Action<SmartLogPodItem>? _onToggled;

    public SmartLogPodItem(Action<SmartLogPodItem>? onToggled) { _onToggled = onToggled; }
    partial void OnIsEnabledChanged(bool value) => _onToggled?.Invoke(this);
}

public class SmartLogGroupItem
{
    public SmartLogDefinition Definition { get; set; } = new();
    public string DisplayText => $"{Definition.Description} — {Definition.LogFilePath}";
    public string ControllerDisplay => $"{Definition.ControllerKind}/{Definition.ControllerName}";
    public ObservableCollection<SmartLogPodItem> Pods { get; } = new();
}

public partial class TabViewModel : ObservableObject, IDisposable
{
    private readonly KubeService _kube = new();
    private readonly BufferService _buffer = new();
    private readonly LogStreamService _streamer;
    private readonly SmartLogStreamService _smartStreamer;
    private readonly DispatcherTimer _drainTimer;
    private readonly DispatcherTimer _newLineTimer;
    private CancellationTokenSource _cts = new();
    private readonly List<LogEntry> _allEntries = new();
    private DateTime _streamStartedAt;
    private List<SmartLogDefinition> _smartLogDefs = new();
    private List<SmartLogSelection>? _savedSmartLogSelections;

    [ObservableProperty] private string _name = "New Tab";
    [ObservableProperty] private bool _isFollowing = true;
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private string _statusText = "Idle";
    [ObservableProperty] private int _lineCount;
    [ObservableProperty] private string _searchText = "";

    public ObservableCollection<string> HideWords { get; } = new();
    public ObservableCollection<string> HighlightWords { get; } = new();

    [ObservableProperty] private bool _optTimestamps = true;
    [ObservableProperty] private bool _optIgnoreErrors = true;
    [ObservableProperty] private bool _optAllContainers = true;
    [ObservableProperty] private bool _optPrefix = true;
    [ObservableProperty] private bool _optWordWrap;
    [ObservableProperty] private bool _isAtBottom = true;
    [ObservableProperty] private int _optMaxLogRequests = 50;
    [ObservableProperty] private bool _optFollow = true;
    [ObservableProperty] private int _optTailLines = 100;
    [ObservableProperty] private BufferMode _bufferMode = BufferMode.Unlimited;
    [ObservableProperty] private int _rollingMaxLines = 100_000;

    public ObservableCollection<FilterItem> FilterNamespaces { get; } = new();
    public ObservableCollection<FilterItem> FilterControllers { get; } = new();
    public ObservableCollection<FilterItem> FilterPodContainers { get; } = new();

    public ObservableCollection<SmartLogGroupItem> SmartLogGroups { get; } = new();

    public ObservableCollection<LogEntry> FilteredEntries { get; } = new();
    public ObservableCollection<LogSource> Sources { get; } = new();
    public ObservableCollection<string> Errors { get; } = new();
    public ClusterInfo? CurrentCluster { get; set; }

    private readonly HashSet<string> _knownNs = new();
    private readonly HashSet<string> _knownCtrl = new();
    private readonly HashSet<string> _knownPodContainer = new();
    private readonly Dictionary<string, HashSet<string>> _ctrlNamespaces = new();
    private readonly Dictionary<string, string> _pcNamespace = new();
    private readonly Dictionary<string, string> _pcController = new();
    private HashSet<string>? _savedUncheckedNs;
    private HashSet<string>? _savedUncheckedCtrl;
    private HashSet<string>? _savedUncheckedContainers;

    public TabViewModel()
    {
        _streamer = new LogStreamService(_kube, _buffer);
        _smartStreamer = new SmartLogStreamService(_kube, _buffer);
        _streamer.OnError += msg =>
        {
            try { System.Windows.Application.Current?.Dispatcher.Invoke(() => Errors.Add(msg)); }
            catch { }
        };
        _smartStreamer.OnError += msg =>
        {
            try { System.Windows.Application.Current?.Dispatcher.Invoke(() => Errors.Add(msg)); }
            catch { }
        };
        _streamer.OnAllCompleted += () =>
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (!IsStreaming) return;
                    IsStreaming = false;
                    StatusText = $"Done. {LineCount} lines.";
                });
            }
            catch { }
        };

        _drainTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _drainTimer.Tick += (_, _) => { try { DrainAndFilter(); } catch { } };
        _drainTimer.Start();

        // Every 10 seconds, clear "new" flag on entries older than 60s
        _newLineTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _newLineTimer.Tick += (_, _) => { try { ClearOldNewFlags(); } catch { } };
        _newLineTimer.Start();
    }

    private void ClearOldNewFlags()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-60);
        foreach (var e in FilteredEntries)
        {
            if (e.IsNew && e.ArrivalTime < cutoff)
                e.IsNew = false;
        }
    }

    private void DrainAndFilter()
    {
        var batch = _buffer.DrainBatch();
        if (batch.Count == 0) return;

        var initialLoadDone = (DateTime.UtcNow - _streamStartedAt).TotalSeconds > 3;
        foreach (var e in batch)
        {
            e.IsNew = initialLoadDone;
            e.ArrivalTime = DateTime.UtcNow;
        }

        _allEntries.AddRange(batch);
        LineCount = _allEntries.Count;

        foreach (var e in batch)
        {
            if (_knownNs.Add(e.Namespace))
            {
                var nsItem = new FilterItem(e.Namespace, OnNamespaceFilterChanged);
                if (_savedUncheckedNs?.Contains(e.Namespace) == true)
                    nsItem.SetSilent(false);
                FilterNamespaces.Add(nsItem);
            }

            // Track controller→namespace mapping before creating items (needed for sibling checks)
            var ck = e.ControllerKey;
            if (!_ctrlNamespaces.TryGetValue(ck, out var nsSet))
                _ctrlNamespaces[ck] = nsSet = new();
            nsSet.Add(e.Namespace);

            if (_knownCtrl.Add(ck))
            {
                var ctrlItem = new FilterItem(ck, OnControllerFilterChanged);
                if (_savedUncheckedCtrl?.Contains(ck) == true)
                    ctrlItem.SetSilent(false);
                else
                {
                    var nsItem = FilterNamespaces.FirstOrDefault(f => f.Name == e.Namespace);
                    if (nsItem != null && !nsItem.IsChecked)
                        ctrlItem.SetSilent(false);
                    // Sibling inherit only when no saved state (manual filtering / pod restart)
                    else if (_savedUncheckedCtrl == null && FilterControllers.Any(f =>
                        _ctrlNamespaces.TryGetValue(f.Name, out var ns) && ns.Contains(e.Namespace) && !f.IsChecked))
                        ctrlItem.SetSilent(false);
                }
                FilterControllers.Add(ctrlItem);
            }

            var cc = e.PodContainer;
            if (_knownPodContainer.Add(cc))
            {
                var pcItem = new FilterItem(cc, ScheduleRebuild);
                if (_savedUncheckedContainers?.Contains(cc) == true)
                    pcItem.SetSilent(false);
                else
                {
                    var pcNsItem = FilterNamespaces.FirstOrDefault(f => f.Name == e.Namespace);
                    var pcCtrlItem = FilterControllers.FirstOrDefault(f => f.Name == ck);
                    if ((pcNsItem != null && !pcNsItem.IsChecked) || (pcCtrlItem != null && !pcCtrlItem.IsChecked))
                        pcItem.SetSilent(false);
                    // Sibling inherit only when no saved state (manual filtering / pod restart)
                    else if (_savedUncheckedContainers == null && FilterPodContainers.Any(f =>
                        _pcController.TryGetValue(f.Name, out var c) && c == ck && !f.IsChecked))
                        pcItem.SetSilent(false);
                }
                FilterPodContainers.Add(pcItem);
            }
            _pcNamespace.TryAdd(cc, e.Namespace);
            _pcController.TryAdd(cc, ck);

            // Update SmartLog groups with newly seen pods
            if (!e.IsSmartLog && SmartLogGroups.Count > 0)
            {
                var src = Sources.FirstOrDefault(s =>
                    s.Namespace == e.Namespace && s.ControllerKind == e.ControllerKind
                    && s.ControllerName == e.Controller && s.ContainerName == e.Container);
                UpdateSmartLogPods(ck, e.Namespace, e.Pod, src);
            }
        }

        // Check if batch has entries older than the last displayed entry
        var lastTs = FilteredEntries.Count > 0
            ? FilteredEntries[FilteredEntries.Count - 1].Timestamp
            : DateTime.MinValue;
        bool outOfOrder = batch.Any(e => e.Timestamp < lastTs);

        if (outOfOrder)
        {
            // Entries arrived out of timestamp order — resort and rebuild
            _allEntries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            FilteredEntries.Clear();
            foreach (var e in _allEntries)
            {
                if (PassesFilter(e))
                {
                    e.IsHighlighted = IsHighlighted(e);
                    FilteredEntries.Add(e);
                }
            }
        }
        else
        {
            // Fast path: batch is newer — sort batch internally and append
            batch.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            foreach (var e in batch)
            {
                if (PassesFilter(e))
                {
                    e.IsHighlighted = IsHighlighted(e);
                    FilteredEntries.Add(e);
                }
            }
        }
    }

    private bool PassesFilter(LogEntry e)
    {
        if (e.IsSmartLog)
        {
            // SmartLog entries are shown only if their pod is still enabled in the SmartLog dropdown
            bool podEnabled = false;
            foreach (var g in SmartLogGroups)
            {
                if (g.Definition.LogFilePath != e.SmartLogFilePath) continue;
                foreach (var p in g.Pods)
                {
                    if (p.Pod == e.Pod && p.Source.Namespace == e.Namespace && p.IsEnabled)
                    { podEnabled = true; break; }
                }
                if (podEnabled) break;
            }
            if (!podEnabled) return false;
        }
        else
        {
            if (FilterNamespaces.Count > 0 && !FilterNamespaces.Any(f => f.Name == e.Namespace && f.IsChecked))
                return false;
            if (FilterControllers.Count > 0 && !FilterControllers.Any(f => f.Name == e.ControllerKey && f.IsChecked))
                return false;
            if (FilterPodContainers.Count > 0 && !FilterPodContainers.Any(f => f.Name == e.PodContainer && f.IsChecked))
                return false;
        }

        foreach (var hw in HideWords)
            if (!string.IsNullOrEmpty(hw) && (e.Message.Contains(hw, StringComparison.OrdinalIgnoreCase)
                || e.RawLine.Contains(hw, StringComparison.OrdinalIgnoreCase)))
                return false;

        if (!string.IsNullOrEmpty(SearchText))
            return MatchesSearch(e);

        return true;
    }

    private bool MatchesSearch(LogEntry e)
    {
        var tokens = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var groups = new List<List<string>>();
        var excludes = new List<string>();
        var current = new List<string>();
        groups.Add(current);

        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Equals("not", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
                excludes.Add(tokens[++i]);
            else if (tokens[i].Equals("or", StringComparison.OrdinalIgnoreCase))
            { /* next word joins current group */ }
            else
            {
                if (i > 0 && tokens[i - 1].Equals("or", StringComparison.OrdinalIgnoreCase))
                    current.Add(tokens[i]);
                else
                {
                    current = new List<string> { tokens[i] };
                    groups.Add(current);
                }
            }
        }

        var text = e.Message + " " + e.RawLine;
        foreach (var ex in excludes)
            if (text.Contains(ex, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var group in groups)
        {
            if (group.Count == 0) continue;
            if (!group.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase))) return false;
        }
        return true;
    }

    private bool IsHighlighted(LogEntry e)
    {
        if (HighlightWords.Count == 0) return false;
        var text = e.Message + " " + e.RawLine;
        return HighlightWords.Any(hw => !string.IsNullOrEmpty(hw) && text.Contains(hw, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSearchTextChanged(string value) => ScheduleRebuild();

    partial void OnOptFollowChanged(bool value)
    {
        if (!value && IsStreaming)
            StopStreaming();
        else if (value && !IsStreaming && Sources.Count > 0)
            _ = StartStreaming();
    }

    private DispatcherTimer? _debounce;
    private void ScheduleRebuild()
    {
        _debounce?.Stop();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _debounce.Tick += (_, _) => { _debounce!.Stop(); RebuildFilteredView(); };
        _debounce.Start();
    }

    private void OnNamespaceFilterChanged()
    {
        var uncheckedNs = FilterNamespaces.Where(f => !f.IsChecked).Select(f => f.Name).ToHashSet();
        var checkedNs = FilterNamespaces.Where(f => f.IsChecked).Select(f => f.Name).ToHashSet();

        foreach (var ctrl in FilterControllers)
        {
            if (_ctrlNamespaces.TryGetValue(ctrl.Name, out var nsSet))
            {
                if (nsSet.All(ns => uncheckedNs.Contains(ns)))
                    ctrl.SetSilent(false);
                else if (nsSet.Any(ns => checkedNs.Contains(ns)) && !ctrl.IsChecked)
                    ctrl.SetSilent(true);
            }
        }

        foreach (var pc in FilterPodContainers)
        {
            if (_pcNamespace.TryGetValue(pc.Name, out var ns))
            {
                if (uncheckedNs.Contains(ns))
                    pc.SetSilent(false);
                else if (checkedNs.Contains(ns) && !pc.IsChecked)
                    pc.SetSilent(true);
            }
        }

        ScheduleRebuild();
    }

    private void OnControllerFilterChanged()
    {
        var checkedNs = FilterNamespaces.Where(f => f.IsChecked).Select(f => f.Name).ToHashSet();

        foreach (var pc in FilterPodContainers)
        {
            if (_pcController.TryGetValue(pc.Name, out var ctrl))
            {
                var ctrlItem = FilterControllers.FirstOrDefault(f => f.Name == ctrl);
                if (ctrlItem != null && !ctrlItem.IsChecked)
                    pc.SetSilent(false);
                else if (ctrlItem != null && ctrlItem.IsChecked && !pc.IsChecked
                         && _pcNamespace.TryGetValue(pc.Name, out var ns) && checkedNs.Contains(ns))
                    pc.SetSilent(true);
            }
        }

        ScheduleRebuild();
    }

    [RelayCommand]
    private void ClearNamespaceFilters()
    {
        foreach (var f in FilterNamespaces) f.SetSilent(false);
        foreach (var f in FilterControllers) f.SetSilent(false);
        foreach (var f in FilterPodContainers) f.SetSilent(false);
        ScheduleRebuild();
    }

    [RelayCommand]
    private void ClearControllerFilters()
    {
        foreach (var f in FilterControllers) f.SetSilent(false);
        foreach (var f in FilterPodContainers) f.SetSilent(false);
        ScheduleRebuild();
    }

    [RelayCommand]
    private void ClearPodContainerFilters()
    {
        foreach (var f in FilterPodContainers) f.SetSilent(false);
        ScheduleRebuild();
    }

    public void SetSmartLogDefinitions(List<SmartLogDefinition> defs)
    {
        _smartLogDefs = defs;
        RefreshSmartLogGroups();
    }

    private void RefreshSmartLogGroups()
    {
        // Find which definitions match current sources
        var existingControllers = Sources
            .Select(s => $"{s.ControllerKind}/{s.ControllerName}")
            .ToHashSet();

        // Track existing groups to avoid re-creating them
        var existingGroupIds = SmartLogGroups.Select(g => g.Definition.Id).ToHashSet();
        var matchingIds = new HashSet<string>();

        foreach (var def in _smartLogDefs)
        {
            // Check if any source matches this definition
            bool matches = Sources.Any(s => SmartLogMatchesSource(def, s));
            if (!matches) continue;

            matchingIds.Add(def.Id);

            if (existingGroupIds.Contains(def.Id))
                continue; // already have this group

            var group = new SmartLogGroupItem { Definition = def };
            SmartLogGroups.Add(group);
        }

        // Remove groups whose definitions no longer match sources
        for (int i = SmartLogGroups.Count - 1; i >= 0; i--)
        {
            if (!matchingIds.Contains(SmartLogGroups[i].Definition.Id))
            {
                // Stop any active streams for this group
                foreach (var pod in SmartLogGroups[i].Pods.Where(p => p.IsEnabled))
                    _smartStreamer.StopStream(pod.Definition, pod.Source, pod.Pod);
                SmartLogGroups.RemoveAt(i);
            }
        }

        // Populate pods from already-known entries
        PopulateSmartLogPodsFromKnown();
    }

    private void PopulateSmartLogPodsFromKnown()
    {
        if (SmartLogGroups.Count == 0) return;

        // Collect unique (namespace, controllerKey, pod, source) tuples from known entries
        var seen = new HashSet<string>();
        foreach (var e in _allEntries)
        {
            if (e.IsSmartLog) continue;
            var key = $"{e.Namespace}/{e.ControllerKey}/{e.Pod}";
            if (!seen.Add(key)) continue;

            var src = Sources.FirstOrDefault(s =>
                s.Namespace == e.Namespace && s.ControllerKind == e.ControllerKind
                && s.ControllerName == e.Controller && s.ContainerName == e.Container);
            UpdateSmartLogPods(e.ControllerKey, e.Namespace, e.Pod, src);
        }
    }

    internal void UpdateSmartLogPods(string controllerKey, string ns, string pod, LogSource? matchingSource)
    {
        foreach (var group in SmartLogGroups)
        {
            var def = group.Definition;
            var src = matchingSource ?? Sources.FirstOrDefault(s => SmartLogMatchesSource(def, s)
                && s.Namespace == ns);
            if (src == null) continue;

            if (!SmartLogMatchesSource(def, src)) continue;

            // Check if this pod already exists in the group
            if (group.Pods.Any(p => p.Pod == pod && p.Source.Namespace == ns))
                continue;

            var podItem = new SmartLogPodItem(OnSmartLogPodToggled)
            {
                Pod = pod,
                Definition = def,
                Source = src
            };

            // Restore saved selection state
            if (_savedSmartLogSelections != null)
            {
                var saved = _savedSmartLogSelections.FirstOrDefault(
                    s => s.DefinitionId == def.Id && s.Pod == pod);
                if (saved != null && saved.IsEnabled)
                    podItem.IsEnabled = true; // will trigger OnSmartLogPodToggled
            }

            group.Pods.Add(podItem);
        }
    }

    private void OnSmartLogPodToggled(SmartLogPodItem item)
    {
        if (IsStreaming)
        {
            if (item.IsEnabled)
                _smartStreamer.StartStream(item.Definition, item.Source, item.Pod, _cts.Token);
            else
                _smartStreamer.StopStream(item.Definition, item.Source, item.Pod);
        }

        // Rebuild so SmartLog entries appear/disappear immediately
        ScheduleRebuild();
    }

    private static bool SmartLogMatchesSource(SmartLogDefinition def, LogSource src)
    {
        return (def.ControllerKind == "*" || def.ControllerKind.Equals(src.ControllerKind, StringComparison.OrdinalIgnoreCase))
            && (def.ControllerName == "*" || def.ControllerName.Equals(src.ControllerName, StringComparison.OrdinalIgnoreCase))
            && (def.ContainerName == "*" || def.ContainerName.Equals(src.ContainerName, StringComparison.OrdinalIgnoreCase));
    }

    private void RebuildFilteredView()
    {
        _allEntries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        FilteredEntries.Clear();
        foreach (var e in _allEntries)
        {
            if (PassesFilter(e))
            {
                e.IsHighlighted = IsHighlighted(e);
                FilteredEntries.Add(e);
            }
        }
    }

    [RelayCommand] private void AddHideWord(string? w) { if (!string.IsNullOrWhiteSpace(w) && !HideWords.Contains(w)) HideWords.Add(w); RebuildFilteredView(); }
    [RelayCommand] private void RemoveHideWord(string? w) { if (w != null) HideWords.Remove(w); RebuildFilteredView(); }
    [RelayCommand] private void AddHighlightWord(string? w) { if (!string.IsNullOrWhiteSpace(w) && !HighlightWords.Contains(w)) HighlightWords.Add(w); RebuildFilteredView(); }
    [RelayCommand] private void RemoveHighlightWord(string? w) { if (w != null) HighlightWords.Remove(w); RebuildFilteredView(); }

    private LogSettings BuildSettings() => new()
    {
        Timestamps = OptTimestamps, IgnoreErrors = OptIgnoreErrors,
        AllContainers = OptAllContainers, Prefix = OptPrefix,
        MaxLogRequests = OptMaxLogRequests, Follow = OptFollow,
        TailLines = OptTailLines > 0 ? OptTailLines : null
    };

    private void SnapshotFilterState()
    {
        var uncNs = FilterNamespaces.Where(f => !f.IsChecked).Select(f => f.Name).ToHashSet();
        var uncCtrl = FilterControllers.Where(f => !f.IsChecked).Select(f => f.Name).ToHashSet();
        var uncPc = FilterPodContainers.Where(f => !f.IsChecked).Select(f => f.Name).ToHashSet();
        _savedUncheckedNs = uncNs.Count > 0 ? uncNs : null;
        _savedUncheckedCtrl = uncCtrl.Count > 0 ? uncCtrl : null;
        _savedUncheckedContainers = uncPc.Count > 0 ? uncPc : null;
    }

    [RelayCommand]
    private async Task StartStreaming()
    {
        if (Sources.Count == 0) return;
        if (IsStreaming) StopStreaming();
        SnapshotFilterState();
        ClearLog();
        _streamStartedAt = DateTime.UtcNow;
        _cts = new CancellationTokenSource();
        _buffer.Mode = BufferMode;
        _buffer.RollingMaxLines = RollingMaxLines;
        _streamer.Settings = BuildSettings();
        IsStreaming = true;
        RefreshSmartLogGroups();
        StatusText = OptFollow
            ? $"Streaming {Sources.Count} sources..."
            : $"Fetching {Sources.Count} sources...";
        try { await _streamer.StartAsync(Sources.ToList(), _cts.Token); }
        catch (Exception ex) { StatusText = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private void StopStreaming()
    {
        _cts.Cancel(); _streamer.StopAll(); _smartStreamer.StopAll();
        IsStreaming = false;
        StatusText = $"Stopped. {LineCount} lines.";
    }

    [RelayCommand]
    private void ClearLog()
    {
        _buffer.Clear(); _allEntries.Clear(); FilteredEntries.Clear();
        FilterNamespaces.Clear(); FilterControllers.Clear(); FilterPodContainers.Clear();
        _knownNs.Clear(); _knownCtrl.Clear(); _knownPodContainer.Clear();
        _ctrlNamespaces.Clear(); _pcNamespace.Clear(); _pcController.Clear();
        _smartStreamer.StopAll();
        foreach (var g in SmartLogGroups)
            g.Pods.Clear();
        SmartLogGroups.Clear();
        LineCount = 0;
    }

    public TabConfig ToConfig()
    {
        var cfg = new TabConfig
        {
            Name = Name, Sources = Sources.ToList(), Settings = BuildSettings(),
            BufferMode = BufferMode, RollingBufferMaxLines = RollingMaxLines,
            UncheckedNamespaces = FilterNamespaces.Where(f => !f.IsChecked).Select(f => f.Name).ToList(),
            UncheckedControllers = FilterControllers.Where(f => !f.IsChecked).Select(f => f.Name).ToList(),
            UncheckedContainers = FilterPodContainers.Where(f => !f.IsChecked).Select(f => f.Name).ToList()
        };
        foreach (var group in SmartLogGroups)
            foreach (var pod in group.Pods.Where(p => p.IsEnabled))
                cfg.SmartLogSelections.Add(new SmartLogSelection
                {
                    DefinitionId = group.Definition.Id,
                    Pod = pod.Pod,
                    IsEnabled = true
                });
        return cfg;
    }

    public void LoadConfig(TabConfig cfg)
    {
        Name = cfg.Name; Sources.Clear();
        foreach (var s in cfg.Sources) Sources.Add(s);
        OptTimestamps = cfg.Settings.Timestamps; OptPrefix = cfg.Settings.Prefix;
        OptFollow = cfg.Settings.Follow; OptTailLines = cfg.Settings.TailLines ?? 100;
        OptMaxLogRequests = cfg.Settings.MaxLogRequests;
        BufferMode = cfg.BufferMode; RollingMaxLines = cfg.RollingBufferMaxLines;
        _savedUncheckedNs = cfg.UncheckedNamespaces.Count > 0 ? cfg.UncheckedNamespaces.ToHashSet() : null;
        _savedUncheckedCtrl = cfg.UncheckedControllers.Count > 0 ? cfg.UncheckedControllers.ToHashSet() : null;
        _savedUncheckedContainers = cfg.UncheckedContainers.Count > 0 ? cfg.UncheckedContainers.ToHashSet() : null;
        _savedSmartLogSelections = cfg.SmartLogSelections.Count > 0 ? cfg.SmartLogSelections : null;
    }

    public KubeService KubeService => _kube;

    public IEnumerable<string> GetFilteredLines() =>
        _allEntries.Where(PassesFilter)
            .Select(e => $"{e.Timestamp:O}\t{e.Namespace}\t{e.Pod}\t{e.Container}\t{e.Message}");

    public void Dispose()
    {
        _drainTimer.Stop(); _newLineTimer.Stop();
        try { _cts.Cancel(); } catch { }
        _streamer.Dispose(); _smartStreamer.Dispose(); _buffer.Dispose(); _kube.Dispose();
    }
}
