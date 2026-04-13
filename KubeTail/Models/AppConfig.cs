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
