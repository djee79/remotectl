using RemoteCtl.Models;
using RemoteCtl.Services;
using RemoteCtl.UI;
using Spectre.Console;

namespace RemoteCtl.CLI;

public class CommandHandler
{
    private readonly ServerRepository  _servers;
    private readonly RecentRepository  _recent;
    private readonly ConnectionService _connection;
    private readonly StatusService     _status;
    private readonly MenuRenderer      _menu;
    private readonly CryptoService     _crypto;

    public CommandHandler(string? configPath = null)
    {
        _servers    = new ServerRepository(configPath);
        _crypto     = new CryptoService(Path.GetDirectoryName(Path.GetFullPath(_servers.ConfigPath))!);
        _servers.SetCrypto(_crypto);
        _recent     = new RecentRepository(_servers.ConfigPath);
        _connection = new ConnectionService(_recent);
        _status     = new StatusService();
        _menu       = new MenuRenderer();
    }

    public async Task RunAsync(string[] args)
    {
        // Strip --wezterm / -w from anywhere in the args
        var useWezterm = args.Contains("--wezterm") || args.Contains("-w");
        var rest = args.Where(a => a is not ("--wezterm" or "-w")).ToArray();

        if (rest.Length == 0)
        {
            RunInteractive(useWezterm);
            return;
        }

        switch (rest[0].ToLowerInvariant())
        {
            case "connect":
                if (rest.Length < 2) { Error("Usage: remotectl connect <name>"); return; }
                Connect(string.Join(" ", rest[1..]), useWezterm);
                break;

            case "list":
                var check = rest.Contains("--check") || rest.Contains("-c");
                await ListAsync(check);
                break;

            case "search":
                if (rest.Length < 2) { Error("Usage: remotectl search <term>"); return; }
                Search(string.Join(" ", rest[1..]));
                break;

            case "recent":
                ShowRecent();
                break;

            case "set-password":
                if (rest.Length < 2) { Error("Usage: remotectl set-password <name>"); return; }
                SetPassword(string.Join(" ", rest[1..]));
                break;

            case "encrypt":
                EncryptPasswords();
                break;

            case "help" or "--help" or "-h":
                PrintHelp();
                break;

            default:
                Error($"Unknown command: {rest[0]}");
                PrintHelp();
                break;
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    private void RunInteractive(bool useWezterm)
    {
        var servers = _servers.Load();
        var selected = _menu.Show(servers);
        if (selected is not null)
            _connection.Connect(selected, useWezterm);
    }

    private void Connect(string name, bool useWezterm)
    {
        var servers = _servers.Load();
        var server  = servers.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (server is null)
        {
            Error($"Server '{name}' not found.");
            AnsiConsole.MarkupLine("[dim]Tip: use [white]remotectl list[/] to see available servers.[/]");
            return;
        }

        _connection.Connect(server, useWezterm);
    }

    private async Task ListAsync(bool checkStatus)
    {
        var servers = _servers.Load();

        if (servers.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No servers found.[/] [dim]Config: {Markup.Escape(_servers.ConfigPath)}[/]");
            return;
        }

        Dictionary<string, bool>? statusMap = null;

        if (checkStatus)
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Checking connectivity…", async _ =>
                {
                    // Check each server on its correct port
                    var tasks = servers.Select(async s =>
                    {
                        var port = s.Port ?? (s.Protocol.Equals("ssh", StringComparison.OrdinalIgnoreCase) ? 22 : 3389);
                        return (s.Host, await _status.IsReachableAsync(s.Host, port));
                    });
                    var results = await Task.WhenAll(tasks);
                    statusMap = results.ToDictionary(r => r.Host, r => r.Item2);
                });
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[dim]●[/]")
            .AddColumn("[bold]Name[/]")
            .AddColumn("[bold]Proto[/]")
            .AddColumn("[bold]Host[/]")
            .AddColumn("[bold]Group[/]")
            .AddColumn("[bold]Tags[/]");

        foreach (var s in servers.OrderBy(s => s.Group).ThenBy(s => s.Name))
        {
            var dot = statusMap is null
                ? "[dim]·[/]"
                : (statusMap.TryGetValue(s.Host, out var up) && up ? "[green]●[/]" : "[red]●[/]");

            var proto = s.Protocol.ToLowerInvariant() switch
            {
                "ssh" => "[cyan]ssh[/]",
                _     => "[blue]rdp[/]",
            };

            var hostDisplay = s.Port is int p ? $"{s.Host}:{p}" : s.Host;

            var tags = s.Tags.Count > 0
                ? string.Join(", ", s.Tags.Select(t => $"[dim]{Markup.Escape(t)}[/]"))
                : "[dim]—[/]";

            table.AddRow(dot, Markup.Escape(s.Name), proto, Markup.Escape(hostDisplay), Markup.Escape(s.Group), tags);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]{servers.Count} server(s)  ·  config: {Markup.Escape(_servers.ConfigPath)}[/]");
    }

    private void Search(string term)
    {
        var servers = _servers.Load();
        var results = servers.Where(s =>
            s.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            s.Host.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            s.Group.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            s.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        if (results.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No servers match '[/][white]{Markup.Escape(term)}[/][yellow]'.[/]");
            return;
        }

        foreach (var s in results.OrderBy(s => s.Group).ThenBy(s => s.Name))
            AnsiConsole.MarkupLine(
                $"[cyan]{Markup.Escape(s.Group)}[/] [dim]/[/] [bold white]{Markup.Escape(s.Name)}[/]  [dim]{Markup.Escape(s.Host)}[/]");

        AnsiConsole.MarkupLine($"\n[dim]{results.Count} result(s)[/]");
    }

    private void ShowRecent()
    {
        var entries  = _recent.Load();
        var serverMap = _servers.Load().ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No recent connections.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Name[/]")
            .AddColumn("[bold]Host[/]")
            .AddColumn("[bold]Group[/]")
            .AddColumn("[bold]When[/]");

        foreach (var e in entries)
        {
            var s = serverMap.TryGetValue(e.ServerName, out var sv) ? sv : null;
            table.AddRow(
                Markup.Escape(e.ServerName),
                Markup.Escape(s?.Host ?? "—"),
                Markup.Escape(s?.Group ?? "—"),
                e.ConnectedAt.ToString("g")
            );
        }

        AnsiConsole.Write(table);
    }

    private void SetPassword(string name)
    {
        var servers = _servers.Load().ToList();
        var server  = servers.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (server is null)
        {
            Error($"Server '{name}' not found.");
            return;
        }

        var password = AnsiConsole.Prompt(
            new TextPrompt<string>($"New password for [bold]{Markup.Escape(server.Name)}[/]:")
                .Secret()
                .AllowEmpty());

        server.Password = password.Length > 0 ? password : null;

        _servers.Save(servers);
        AnsiConsole.MarkupLine(password.Length > 0
            ? $"[green]Password saved (encrypted) for[/] [bold]{Markup.Escape(server.Name)}[/]."
            : $"[yellow]Password cleared for[/] [bold]{Markup.Escape(server.Name)}[/].");
    }

    private void EncryptPasswords()
    {
        var servers   = _servers.Load().ToList();
        var withCreds = servers.Where(s => s.Password is { Length: > 0 }).ToList();

        if (withCreds.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No passwords found in servers.json.[/]");
            return;
        }

        // Save() will encrypt all non-null passwords via CryptoService
        _servers.Save(servers);

        AnsiConsole.MarkupLine($"[green]Encrypted {withCreds.Count} password(s)[/] in [dim]{Markup.Escape(_servers.ConfigPath)}[/].");
        AnsiConsole.MarkupLine($"[dim]Key stored at: {Markup.Escape(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_servers.ConfigPath))!, "key"))}[/]");

        foreach (var s in withCreds)
            AnsiConsole.MarkupLine($"  [dim]·[/] {Markup.Escape(s.Name)}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void PrintHelp()
    {
        // ── Header ────────────────────────────────────────────────────────────
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold steelblue1] RemoteCtl [/][dim]— Terminal Remote Connection Manager[/]")
            .RuleStyle("steelblue1 dim").LeftJustified());
        AnsiConsole.WriteLine();

        // ── Commands ──────────────────────────────────────────────────────────
        var commands = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Color.Grey))
            .AddColumn(new TableColumn("[bold]Command[/]").Width(38))
            .AddColumn(new TableColumn("[bold]Description[/]"));

        commands.AddRow("[cyan]remotectl[/]",                              "Open interactive server picker (default)");
        commands.AddRow("[cyan]remotectl connect [white]<name>[/][/]",     "Connect directly to a server by name");
        commands.AddRow("[cyan]remotectl list[/]",                         "List all configured servers");
        commands.AddRow("[cyan]remotectl list --check[/]",                 "List servers with live port reachability check");
        commands.AddRow("[cyan]remotectl search [white]<term>[/][/]",      "Search by name, host, group, or tag");
        commands.AddRow("[cyan]remotectl recent[/]",                       "Show last 20 connected servers");
        commands.AddRow("[cyan]remotectl set-password [white]<name>[/][/]","Securely set/update a server's password");
        commands.AddRow("[cyan]remotectl encrypt[/]",                      "Encrypt all plaintext passwords in servers.json");
        commands.AddRow("[cyan]remotectl help[/]",                         "Show this help screen");

        AnsiConsole.Write(new Panel(commands)
            .Header("[bold] Commands [/]")
            .BorderStyle(new Style(Color.SteelBlue1)));

        // ── Interactive UI ────────────────────────────────────────────────────
        var ui = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Color.Grey))
            .AddColumn(new TableColumn("[bold]Key[/]").Width(18))
            .AddColumn(new TableColumn("[bold]Action[/]"));

        ui.AddRow("[yellow]↑ / ↓[/]",        "Navigate the server list");
        ui.AddRow("[yellow]Enter[/]",         "Connect to the selected server");
        ui.AddRow("[yellow]Esc[/]",           "Exit without connecting");
        ui.AddRow("[yellow]Any text[/]",      "Filter by name, host, group, or tag (live)");
        ui.AddRow("[yellow]Backspace[/]",     "Delete last character from filter");
        ui.AddRow("[yellow]#tagname[/]",      "Filter by tag — e.g. [dim]#production[/]");
        ui.AddRow("[yellow]text #tag[/]",     "Combine text and tag filters — e.g. [dim]plant #critical[/]");

        AnsiConsole.Write(new Panel(ui)
            .Header("[bold] Interactive Picker (remotectl with no args) [/]")
            .BorderStyle(new Style(Color.SteelBlue1)));

        // ── Flags ─────────────────────────────────────────────────────────────
        var flags = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Color.Grey))
            .AddColumn(new TableColumn("[bold]Flag[/]").Width(18))
            .AddColumn(new TableColumn("[bold]Effect[/]"));

        flags.AddRow("[yellow]--wezterm[/], [yellow]-w[/]",  "Spawn the connection in a new WezTerm tab");
        flags.AddRow("[yellow]--config [white]<path>[/][/]", "Override the path to servers.json");

        AnsiConsole.Write(new Panel(flags)
            .Header("[bold] Global Flags [/]")
            .BorderStyle(new Style(Color.SteelBlue1)));

        // ── servers.json format ───────────────────────────────────────────────
        var schema = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Color.Grey))
            .AddColumn(new TableColumn("[bold]Field[/]").Width(14))
            .AddColumn(new TableColumn("[bold]Type[/]").Width(12))
            .AddColumn(new TableColumn("[bold]Required[/]").Width(10))
            .AddColumn(new TableColumn("[bold]Description[/]"));

        schema.AddRow("[white]Name[/]",     "string",  "[green]yes[/]", "Unique display name");
        schema.AddRow("[white]Host[/]",     "string",  "[green]yes[/]", "IP address or hostname");
        schema.AddRow("[white]Group[/]",    "string",  "[green]yes[/]", "Hierarchy path, e.g. [dim]Plant A / Line 1[/]");
        schema.AddRow("[white]Protocol[/]", "string",  "[green]yes[/]", "[dim]rdp[/] or [dim]ssh[/]");
        schema.AddRow("[white]Tags[/]",     "string[[]]","[dim]no[/]",  "Free-form labels for filtering, e.g. [dim][[\"production\",\"critical\"]][/]");
        schema.AddRow("[white]Username[/]", "string",  "[dim]no[/]",    "Login username");
        schema.AddRow("[white]Password[/]", "string",  "[dim]no[/]",    "Plaintext (then run [cyan]encrypt[/]) or pre-encrypted [dim]enc:…[/]");
        schema.AddRow("[white]Port[/]",     "int",     "[dim]no[/]",    "Custom port — defaults to 3389 (RDP) or 22 (SSH)");

        AnsiConsole.Write(new Panel(schema)
            .Header("[bold] servers.json — Field Reference [/]")
            .BorderStyle(new Style(Color.SteelBlue1)));

        // ── Credential storage ────────────────────────────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold]Passwords at rest[/] are AES-256-GCM encrypted.\n" +
            "The key is auto-generated on first use and stored at:\n" +
            "  [dim]~/.config/remotectl/key[/]  [green](chmod 600)[/]\n\n" +
            "[bold]Workflow:[/]\n" +
            "  1. Add [white]\"Password\": \"yourpassword\"[/] to servers.json\n" +
            "  2. Run [cyan]remotectl encrypt[/] → passwords become [dim]enc:…[/] in the file\n" +
            "  3. Or use [cyan]remotectl set-password <name>[/] for a hidden prompt (never touches disk as plaintext)\n\n" +
            "[dim]Tip: keep [white]key[/] out of version control. servers.json is safe to commit once encrypted.[/]")
            .Header("[bold] Credential Storage [/]")
            .BorderStyle(new Style(Color.SteelBlue1)));

        // ── Config & env ──────────────────────────────────────────────────────
        var configPath = Path.GetFullPath(_servers.ConfigPath);
        var keyPath    = Path.Combine(Path.GetDirectoryName(configPath)!, "key");
        var recentPath = Path.Combine(Path.GetDirectoryName(configPath)!, "recent.json");

        AnsiConsole.Write(new Panel(
            $"[bold]Active config:[/]  [white]{Markup.Escape(configPath)}[/]\n" +
            $"[bold]Encryption key:[/] [white]{Markup.Escape(keyPath)}[/]\n" +
            $"[bold]Recent history:[/] [white]{Markup.Escape(recentPath)}[/]\n\n" +
            "[bold]Resolution order:[/]\n" +
            "  1. [yellow]$REMOTECTL_CONFIG[/] environment variable\n" +
            "  2. [dim]./servers.json[/]  (current directory)\n" +
            "  3. [dim]~/.config/remotectl/servers.json[/]")
            .Header("[bold] Configuration [/]")
            .BorderStyle(new Style(Color.SteelBlue1)));

        AnsiConsole.WriteLine();
    }

    private static void Error(string msg) =>
        AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(msg)}");
}
