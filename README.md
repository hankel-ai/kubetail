# KubeTail

A Windows desktop app for tailing Kubernetes pod logs in real time across multiple clusters, namespaces, and controllers. Built with WPF and .NET 8.

## Features

### Multi-Source Log Streaming
- Browse and select log sources through a 5-column drill-down: Cluster > Namespace > Controller > Pod > Container
- Stream logs from multiple controllers and containers simultaneously
- Automatic reconnection when pods restart or roll — new pods are picked up, dead ones are dropped
- Follow mode for real-time tailing, or fetch a fixed snapshot

### Filtering
- **Namespace / Controller / Container dropdowns** — check/uncheck items to filter the log view in real time
  - Cascading selection: unchecking a namespace automatically unchecks its controllers and containers
  - Clear button in each dropdown to deselect all
  - Controller dropdown shows the resource kind (e.g. `Deployment/nginx`, `StatefulSet/redis`)
  - Filter selections are stable across pod restarts (keyed by controller/container, not ephemeral pod names)
- **Search** — AND by default, `or` between words for OR, `not word` to exclude
- **Hide words** — tag words to suppress matching lines
- **Highlight words** — tag words to highlight matching lines in yellow

### Tabs and Profiles
- Multiple tabs, each with independent sources, filters, and settings
- Save/load named profiles that persist sources, settings, and filter selections
- Last-used profile is remembered across app restarts

### Display Options
- Source prefix tags with per-source color coding
- Timestamps (toggle on/off)
- Word wrap (toggle on/off)
- Auto-scroll with bottom indicator
- New log lines highlighted with a green left border (fades after 60s)
- Configurable tail line count

### Other
- Right-click to copy selected lines to clipboard
- Export filtered logs to `.log` or `.csv`
- Cluster manager for adding/editing Kubernetes contexts
- Namespace exclusion patterns (e.g. `kube-*`, `openshift*`)
- Dark theme throughout

## Requirements

- Windows 10/11 (x64)
- .NET 8 SDK (for building from source)
- A valid `~/.kube/config` with one or more cluster contexts

## Build

```bash
dotnet build
```

## Publish

Produces a self-contained single-file executable — no .NET runtime needed on the target machine.

```bash
dotnet publish -c Release -o publish
```

The output is `publish/KubeTail.exe`.

## Usage

1. Launch `KubeTail.exe`
2. Click **Clusters** to verify your Kubernetes contexts are detected (pulled from `~/.kube/config`)
3. Click **Sources** on a tab to open the source picker — select a cluster, then drill into namespaces, controllers, pods, and containers
4. Click **OK** — streaming starts automatically
5. Use the filter dropdowns (Namespace, Controller, Container) to narrow the live view
6. Save your setup with **Save Profile** for quick access later

## Tech Stack

- .NET 8 / WPF (C#)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM source generators
- [KubernetesClient](https://github.com/kubernetes-client/csharp) 19.0.2 — Kubernetes API
- Self-contained single-file publish (win-x64)

## Project Structure

```
kubetail/
  KubeTail.sln
  KubeTail/
    App.xaml(.cs)              # Entry point, global styles
    Models/
      LogEntry.cs              # Single log line model
      LogSource.cs             # Source definition, tab config, settings
      AppConfig.cs             # Persisted app config (clusters, profiles, window state)
      ClusterInfo.cs           # Cluster connection info
      TreeNodes.cs             # Tree model for the source picker dialog
    Services/
      KubeService.cs           # Kubernetes API wrapper
      LogStreamService.cs      # Pod log streaming with retry and reconnect
      BufferService.cs         # Thread-safe log buffer (unlimited or rolling)
      ConfigService.cs         # JSON config persistence
    ViewModels/
      MainViewModel.cs         # Tabs, profiles, cluster list
      TabViewModel.cs          # Per-tab: streaming, filters, search, display options
      ConfigureSourcesViewModel.cs  # Source picker drill-down logic
    Views/
      MainWindow.xaml(.cs)     # Shell: tab bar, toolbar, status bar
      LogTabView.xaml(.cs)     # Log display, filter dropdowns, search/hide/highlight
      ConfigureSourcesDialog.xaml(.cs)  # 5-column source picker
      ClusterManagerDialog.xaml(.cs)
      ExclusionsDialog.xaml(.cs)
      InputDialog.xaml(.cs)
    Converters/
      Converters.cs            # Value converters (color, visibility, wrap)
```

## Configuration

App settings are stored in `%APPDATA%/KubeTail/config.json` and include:

- Saved cluster connections
- Saved tab profiles (sources, settings, filter state)
- Namespace exclusion patterns
- Window size and position
