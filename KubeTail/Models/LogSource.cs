using System.Text.Json.Serialization;

namespace KubeTail.Models;

public class LogSource
{
    public string ClusterContext { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string ControllerKind { get; set; } = "";
    public string ControllerName { get; set; } = "";
    public string ContainerName { get; set; } = "";

    [JsonIgnore]
    public string Key => $"{ClusterContext}/{Namespace}/{ControllerKind}/{ControllerName}/{ContainerName}";
}

public class LogSettings
{
    public bool Timestamps { get; set; } = true;
    public bool IgnoreErrors { get; set; } = true;
    public bool AllContainers { get; set; } = true;
    public bool Prefix { get; set; } = true;
    public int MaxLogRequests { get; set; } = 50;
    public bool Follow { get; set; } = true;
    public int? TailLines { get; set; } = 100;
}

public class TabConfig
{
    public string Name { get; set; } = "New Tab";
    public List<LogSource> Sources { get; set; } = new();
    public LogSettings Settings { get; set; } = new();
    public BufferMode BufferMode { get; set; } = BufferMode.Unlimited;
    public int RollingBufferMaxLines { get; set; } = 100_000;
    public List<string> UncheckedNamespaces { get; set; } = new();
    public List<string> UncheckedControllers { get; set; } = new();
    public List<string> UncheckedContainers { get; set; } = new();
    public List<SmartLogSelection> SmartLogSelections { get; set; } = new();
}

public class SmartLogDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Description { get; set; } = "";
    public string ControllerKind { get; set; } = "";
    public string ControllerName { get; set; } = "";
    public string ContainerName { get; set; } = "";
    public string LogFilePath { get; set; } = "";
    public bool IsBuiltIn { get; set; }

    public string MatchKey => $"{ControllerKind}/{ControllerName}";
}

public class SmartLogSelection
{
    public string DefinitionId { get; set; } = "";
    public string Pod { get; set; } = "";
    public bool IsEnabled { get; set; }
}

public enum BufferMode { Unlimited, Rolling }
