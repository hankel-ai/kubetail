using System.Text.Json.Serialization;

namespace KubeTail.Models;

public class ClusterInfo
{
    public string ContextName { get; set; } = "";
    public string Description { get; set; } = "";
    public string KubeConfigPath { get; set; } = "";
    public string ClusterServer { get; set; } = "";

    /// <summary>Shows description if configured, otherwise context name.</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Description)
        ? ContextName
        : Description;

    /// <summary>For the cluster list in Configure Sources — shows only the server endpoint.</summary>
    [JsonIgnore]
    public string EndpointDisplay => string.IsNullOrWhiteSpace(Description)
        ? $"{ContextName}  →  {ClusterServer}"
        : $"{Description}  →  {ClusterServer}";
}
