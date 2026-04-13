using KubeTail.Models;
using System.IO;
using System.Text.Json;

namespace KubeTail.Services;

public class ConfigService
{
    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KubeTail");
    private static readonly string ConfigPath = Path.Combine(AppDir, "config.json");
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath)) return new();
        try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Opts) ?? new(); }
        catch { return new(); }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(AppDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Opts));
    }

    public void SaveClusters(List<ClusterInfo> clusters)
    {
        var c = Load(); c.SavedClusters = clusters; Save(c);
    }

    public void SaveTabProfile(string name, List<TabConfig> tabs)
    {
        var c = Load();
        c.SavedTabProfiles.RemoveAll(p => p.ProfileName == name);
        c.SavedTabProfiles.Add(new SavedTabProfile
        {
            ProfileName = name, SavedAt = DateTime.Now, Tabs = tabs
        });
        Save(c);
    }

    public List<string> GetNamespaceExclusions() => Load().NamespaceExclusions;

    public void SaveNamespaceExclusions(List<string> exclusions)
    {
        var c = Load(); c.NamespaceExclusions = exclusions; Save(c);
    }
}
