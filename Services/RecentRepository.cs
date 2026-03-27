using System.Text.Json;
using RemoteCtl.Models;

namespace RemoteCtl.Services;

public class RecentRepository
{
    private const int MaxEntries = 20;

    private readonly string _path;

    public RecentRepository(string configPath)
    {
        _path = Path.Combine(Path.GetDirectoryName(configPath)!, "recent.json");
    }

    public List<RecentEntry> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize(json, JsonContext.Default.ListRecentEntry) ?? [];
        }
        catch { return []; }
    }

    public void Add(string serverName)
    {
        var entries = Load();
        entries.RemoveAll(e => e.ServerName.Equals(serverName, StringComparison.OrdinalIgnoreCase));
        entries.Insert(0, new RecentEntry { ServerName = serverName, ConnectedAt = DateTime.Now });
        if (entries.Count > MaxEntries) entries = entries[..MaxEntries];

        try { File.WriteAllText(_path, JsonSerializer.Serialize(entries, JsonContext.Default.ListRecentEntry)); }
        catch { /* non-fatal */ }
    }
}
