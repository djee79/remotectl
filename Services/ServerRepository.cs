using System.Text.Json;
using RemoteCtl.Models;

namespace RemoteCtl.Services;

public class ServerRepository
{
    private List<Server>? _cache;
    private CryptoService? _crypto;

    public ServerRepository(string? path = null)
    {
        ConfigPath = path
            ?? Environment.GetEnvironmentVariable("REMOTECTL_CONFIG")
            ?? ResolveDefaultPath();
    }

    public string ConfigPath { get; }

    /// <summary>Inject crypto after construction (avoids circular dependency on config dir).</summary>
    public void SetCrypto(CryptoService crypto) => _crypto = crypto;

    public IReadOnlyList<Server> Load()
    {
        if (_cache is not null) return _cache;

        if (!File.Exists(ConfigPath))
        {
            _cache = [];
            return _cache;
        }

        var json = File.ReadAllText(ConfigPath);
        _cache = JsonSerializer.Deserialize(json, JsonContext.Default.ListServer) ?? [];

        // Transparently decrypt any stored passwords
        if (_crypto is not null)
        {
            var stale = new List<string>();
            foreach (var s in _cache.Where(s => _crypto.IsEncrypted(s.Password)))
            {
                try
                {
                    s.Password = _crypto.Decrypt(s.Password!);
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    // Key mismatch — password was encrypted with a different key
                    stale.Add(s.Name);
                    s.Password = null;
                }
            }

            if (stale.Count > 0)
            {
                Spectre.Console.AnsiConsole.MarkupLine(
                    $"[yellow]Warning:[/] {stale.Count} password(s) could not be decrypted (wrong key) and were cleared:");
                foreach (var name in stale)
                    Spectre.Console.AnsiConsole.MarkupLine($"  [dim]·[/] {Spectre.Console.Markup.Escape(name)}");
                Spectre.Console.AnsiConsole.MarkupLine(
                    "[dim]Run [white]remotectl set-password <name>[/] to re-enter them.[/]");
            }
        }

        return _cache;
    }

    /// <summary>
    /// Persists the server list. Passwords are encrypted if a CryptoService is set.
    /// Clears the in-memory cache so the next Load() re-reads the saved file.
    /// </summary>
    public void Save(IReadOnlyList<Server> servers)
    {
        var toWrite = _crypto is null
            ? [.. servers]
            : servers.Select(s => new Server
            {
                Name     = s.Name,
                Host     = s.Host,
                Group    = s.Group,
                Tags     = [.. s.Tags],
                Username = s.Username,
                Password = s.Password is { Length: > 0 } ? _crypto.Encrypt(s.Password) : s.Password,
                Port     = s.Port,
                Protocol = s.Protocol,
            }).ToList();

        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(toWrite, JsonContext.Default.ListServer));
        _cache = null; // invalidate so next Load() reflects the saved state
    }

    private static string ResolveDefaultPath()
    {
        var local = Path.Combine(Directory.GetCurrentDirectory(), "servers.json");
        if (File.Exists(local)) return local;

        var configDir = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "remotectl")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "remotectl");

        Directory.CreateDirectory(configDir);
        return Path.Combine(configDir, "servers.json");
    }
}
