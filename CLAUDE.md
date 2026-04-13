# KubeTail — Kubernetes Log Viewer

## Purpose
WPF desktop app for tailing Kubernetes pod logs across multiple clusters, namespaces, and controllers. Supports multi-tab, real-time streaming, filtering, search, and export.

## Tech Stack
- .NET 8 / WPF (C#)
- CommunityToolkit.Mvvm (MVVM source generators)
- KubernetesClient 19.0.2
- Self-contained single-file publish (win-x64)

## Build / Run / Publish
```bash
# Build (use local dotnet if system dotnet not found)
dotnet build
# or: "$HOME/AppData/Local/dotnet/dotnet.exe" build

# Publish
dotnet publish -c Release -o publish
```

## Project Structure
```
kubetail/
  KubeTail.sln
  KubeTail/
    App.xaml(.cs)            # App entry point, global styles
    Models/
      LogEntry.cs            # Single log line (ObservableObject, IsNew/IsHighlighted)
      LogSource.cs           # Pod/container source definition
      AppConfig.cs           # Serializable app config / profiles
      ClusterInfo.cs         # Cluster connection info
      TreeNodes.cs           # UI tree model for source picker
    Services/
      KubeService.cs         # Kubernetes API wrapper (logs + exec WebSocket)
      LogStreamService.cs    # Manages pod log streams, retry logic, completion events
      SmartLogStreamService.cs # Exec-based file tailing inside pods (tail -F)
      BufferService.cs       # Thread-safe log buffer with rolling/spill modes
      ConfigService.cs       # Persists app config to JSON
    ViewModels/
      MainViewModel.cs       # Tabs, profiles, cluster list
      TabViewModel.cs        # Per-tab state: streaming, filters, search, options
      ConfigureSourcesViewModel.cs
    Views/
      MainWindow.xaml(.cs)   # Shell: tab bar, toolbar, status bar
      LogTabView.xaml(.cs)   # Log display, controls, toast overlay, copy
      ConfigureSourcesDialog.xaml(.cs)
      ClusterManagerDialog.xaml(.cs)
      ExclusionsDialog.xaml(.cs)
      SmartLogConfigDialog.xaml(.cs)  # Manage SmartLog file-tailing definitions
      InputDialog.xaml(.cs)
    Converters/
      Converters.cs          # Bool/color/wrap/highlight converters
```

## Key Conventions
- MVVM via CommunityToolkit source generators ([ObservableProperty], [RelayCommand])
- Dark theme (#1E1E1E background, VS Code-inspired)
- Right-click on log list = copy selected lines to clipboard + toast
- New log lines get green left-border highlight (only after initial tail load, fades after 60s)
- Follow checkbox controls --follow flag; unchecking stops stream
- Start always clears logs and starts fresh
- LogStreamService fires OnAllCompleted when all streams end naturally (non-follow mode)

## Filter Architecture
- 3-level cascading filters: Namespace > Controller (Kind/Name) > Container (Pod/Container)
- Container filter keyed by PodContainer (pod-name/container-name) for per-pod filtering
- FilterItem.SetSilent() suppresses change callbacks during cascade operations
- Saved unchecked state in profiles; sibling inherit gated on `_savedUnchecked* == null`
- Relationship tracking: `_ctrlNamespaces`, `_pcNamespace`, `_pcController` dictionaries

## SmartLog Feature
- Tails log files inside pods via `kubectl exec ... tail -F <path>` (WebSocket exec API)
- SmartLogDefinition: maps controller kind/name + container to a log file path
- Wildcard support: `*` in controller/container fields and file paths (shell-wrapped)
- Definitions persisted in AppConfig; selections persisted per-profile in TabConfig
- SmartLog dropdown (amber colored) shows only definitions matching current sources
- Pods appear dynamically as log entries arrive; never enabled by default
- SmartLogStreamService manages exec WebSocket streams with retry logic
- Default definitions: NGINX, Apache, syslog, application logs
