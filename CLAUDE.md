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
      KubeService.cs         # Kubernetes API wrapper
      LogStreamService.cs    # Manages pod log streams, retry logic, completion events
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
