using k8s;
using k8s.Models;
using KubeTail.Models;
using System.IO;

namespace KubeTail.Services;

public class KubeService : IDisposable
{
    private Kubernetes? _client;

    public void Connect(ClusterInfo cluster)
    {
        var path = string.IsNullOrEmpty(cluster.KubeConfigPath)
            ? KubernetesClientConfiguration.KubeConfigDefaultLocation
            : cluster.KubeConfigPath;
        var cfg = KubernetesClientConfiguration.BuildConfigFromConfigFile(path, cluster.ContextName);
        _client?.Dispose();
        _client = new Kubernetes(cfg);
    }

    public static List<ClusterInfo> ReadContexts(string? path = null)
    {
        var p = path ?? KubernetesClientConfiguration.KubeConfigDefaultLocation;
        if (!File.Exists(p)) return new();
        var cfg = KubernetesClientConfiguration.LoadKubeConfig(p);
        return cfg.Contexts.Select(c => new ClusterInfo
        {
            ContextName = c.Name, KubeConfigPath = p,
            ClusterServer = cfg.Clusters
                .FirstOrDefault(cl => cl.Name == c.ContextDetails?.Cluster)
                ?.ClusterEndpoint?.Server ?? ""
        }).ToList();
    }

    public static string? GetCurrentContext(string? path = null)
    {
        var p = path ?? KubernetesClientConfiguration.KubeConfigDefaultLocation;
        if (!File.Exists(p)) return null;
        var cfg = KubernetesClientConfiguration.LoadKubeConfig(p);
        return cfg.CurrentContext;
    }

    public async Task<List<string>> GetNamespacesAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var ns = await _client!.CoreV1.ListNamespaceAsync(cancellationToken: ct);
        return ns.Items.Select(n => n.Metadata.Name).OrderBy(n => n).ToList();
    }

    public async Task<List<ControllerInfo>> GetControllersAsync(string ns, CancellationToken ct = default)
    {
        EnsureConnected();
        var pods = await _client!.CoreV1.ListNamespacedPodAsync(ns, cancellationToken: ct);
        var map = new Dictionary<string, ControllerInfo>();
        foreach (var pod in pods.Items)
        {
            var (kind, name) = ResolveController(pod);
            var key = $"{kind}/{name}";
            if (!map.ContainsKey(key))
                map[key] = new ControllerInfo { Kind = kind, Name = name, PodCount = 0 };
            map[key].PodCount++;
        }
        return map.Values.OrderBy(c => c.Kind).ThenBy(c => c.Name).ToList();
    }

    public async Task<List<PodInfo>> GetPodsForControllerDetailAsync(
        string ns, string ctrlKind, string ctrlName, CancellationToken ct = default)
    {
        EnsureConnected();
        var pods = await _client!.CoreV1.ListNamespacedPodAsync(ns, cancellationToken: ct);
        return pods.Items
            .Where(p => { var (k, n) = ResolveController(p); return k == ctrlKind && n == ctrlName; })
            .Select(p => new PodInfo
            {
                Name = p.Metadata.Name,
                Status = p.Status?.Phase ?? "Unknown",
                ContainerCount = p.Spec?.Containers?.Count ?? 0
            })
            .OrderBy(p => p.Name).ToList();
    }

    public async Task<List<string>> GetContainersForPodAsync(
        string ns, string podName, CancellationToken ct = default)
    {
        EnsureConnected();
        var pod = await _client!.CoreV1.ReadNamespacedPodAsync(podName, ns, cancellationToken: ct);
        return pod.Spec?.Containers?.Select(c => c.Name).OrderBy(n => n).ToList() ?? new();
    }

    public async Task<List<string>> GetPodsForControllerAsync(
        string ns, string ctrlKind, string ctrlName, CancellationToken ct = default)
    {
        EnsureConnected();
        var pods = await _client!.CoreV1.ListNamespacedPodAsync(ns, cancellationToken: ct);
        return pods.Items
            .Where(p => { var (k, n) = ResolveController(p); return k == ctrlKind && n == ctrlName; })
            .Select(p => p.Metadata.Name).ToList();
    }

    public async Task<Stream> StreamPodLogAsync(
        string ns, string pod, string container, LogSettings s, CancellationToken ct)
    {
        EnsureConnected();
        var response = await _client!.CoreV1.ReadNamespacedPodLogWithHttpMessagesAsync(
            pod, ns, container: container, follow: s.Follow,
            timestamps: s.Timestamps, tailLines: s.TailLines,
            cancellationToken: ct);
        return response.Body;
    }

    private static (string Kind, string Name) ResolveController(V1Pod pod)
    {
        var owners = pod.Metadata?.OwnerReferences;
        if (owners == null || owners.Count == 0)
            return ("Pod", pod.Metadata?.Name ?? "unknown");
        var owner = owners[0];
        if (owner.Kind == "ReplicaSet")
        {
            var rs = owner.Name;
            var idx = rs.LastIndexOf('-');
            return ("Deployment", idx > 0 ? rs[..idx] : rs);
        }
        return (owner.Kind, owner.Name);
    }

    private void EnsureConnected()
    {
        if (_client == null) throw new InvalidOperationException("Not connected.");
    }

    public void Dispose() => _client?.Dispose();
}

public class ControllerInfo
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public int PodCount { get; set; }
    public string Display => $"{Kind}/{Name} ({PodCount} pods)";
}

public class PodInfo
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public int ContainerCount { get; set; }
    public string Display => $"{Name} [{Status}] ({ContainerCount}c)";
}
