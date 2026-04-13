using System.Text.RegularExpressions;

namespace KubeTail.Models;

public class AppConfig
{
    public List<ClusterInfo> SavedClusters { get; set; } = new();
    public List<SavedTabProfile> SavedTabProfiles { get; set; } = new();
    public List<string> NamespaceExclusions { get; set; } = new()
    {
        "openshift*", "kube-*", "default"
    };
    public List<SmartLogDefinition> SmartLogDefinitions { get; set; } = new();
    public string? LastProfileName { get; set; }
    public bool IsMaximized { get; set; }
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
}

public class SavedTabProfile
{
    public string ProfileName { get; set; } = "";
    public DateTime SavedAt { get; set; }
    public List<TabConfig> Tabs { get; set; } = new();
}

public static class SmartLogDefaults
{
    public static List<SmartLogDefinition> Create() => new()
    {
        new() { Description = "NGINX access log", ControllerKind = "Deployment", ControllerName = "nginx", ContainerName = "nginx", LogFilePath = "/var/log/nginx/access.log", IsBuiltIn = true },
        new() { Description = "NGINX error log", ControllerKind = "Deployment", ControllerName = "nginx", ContainerName = "nginx", LogFilePath = "/var/log/nginx/error.log", IsBuiltIn = true },
        new() { Description = "Apache access log", ControllerKind = "Deployment", ControllerName = "apache", ContainerName = "apache", LogFilePath = "/var/log/apache2/access.log", IsBuiltIn = true },
        new() { Description = "Apache error log", ControllerKind = "Deployment", ControllerName = "apache", ContainerName = "apache", LogFilePath = "/var/log/apache2/error.log", IsBuiltIn = true },
        new() { Description = "Application logs", ControllerKind = "*", ControllerName = "*", ContainerName = "*", LogFilePath = "/var/log/app/*.log", IsBuiltIn = true },
        new() { Description = "Syslog", ControllerKind = "*", ControllerName = "*", ContainerName = "*", LogFilePath = "/var/log/syslog", IsBuiltIn = true },
        new() { Description = "Messages log", ControllerKind = "*", ControllerName = "*", ContainerName = "*", LogFilePath = "/var/log/messages", IsBuiltIn = true },
    };
}

public static class WildcardMatcher
{
    public static bool IsExcluded(string name, List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            if (Regex.IsMatch(name, regex, RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }
}
