using RemoteCtl.Models;
using Spectre.Console;

namespace RemoteCtl.UI;

public static class ServerEditor
{
    // ── Public entry points ───────────────────────────────────────────────────

    public static Server? Edit(Server original)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold steelblue1] Edit Server:[/] [bold]{Markup.Escape(original.Name)}[/]");
        AnsiConsole.MarkupLine("[dim]Enter = keep current value  ·  optional fields: type new value or leave empty to keep[/]");
        AnsiConsole.WriteLine();

        // Required fields — DefaultValue so Enter keeps current
        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Name[/]")
                .DefaultValue(original.Name));

        var host = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Host[/]")
                .DefaultValue(original.Host));

        var group = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Group[/]")
                .DefaultValue(original.Group));

        var protocol = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Protocol[/] [dim](rdp/ssh)[/]")
                .DefaultValue(original.Protocol.ToLowerInvariant())
                .Validate(v => v is "rdp" or "ssh"
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be rdp or ssh[/]")));

        // Optional string fields — show current in label, empty = keep
        var username = AnsiConsole.Prompt(
            OptionalField("Username", original.Username));
        var finalUsername = username.Length > 0 ? username.Trim() : original.Username;

        // Password — secret, empty = keep existing
        var pwHint = original.Password is not null ? "empty = keep existing" : "empty = none";
        var password = AnsiConsole.Prompt(
            new TextPrompt<string>($"[bold]Password[/] [dim]({pwHint})[/]")
                .Secret()
                .AllowEmpty());
        var finalPassword = password.Length > 0 ? password : original.Password;

        // Port — empty = keep current (or default)
        var portHint = original.Port is int op ? op.ToString() : "default 3389/22";
        var portStr = AnsiConsole.Prompt(
            new TextPrompt<string>($"[bold]Port[/] [dim](current: {portHint}, empty = keep)[/]")
                .AllowEmpty());
        var finalPort = portStr.Length > 0 && int.TryParse(portStr, out var pp) ? pp : original.Port;

        // Tags — comma-separated, empty = keep current
        var tagsHint = original.Tags.Count > 0 ? string.Join(", ", original.Tags) : "none";
        var tagsStr = AnsiConsole.Prompt(
            new TextPrompt<string>($"[bold]Tags[/] [dim](current: {Markup.Escape(tagsHint)}, comma-separated, empty = keep)[/]")
                .AllowEmpty());
        var finalTags = tagsStr.Length > 0
            ? [.. tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : original.Tags;

        AnsiConsole.WriteLine();
        if (!AnsiConsole.Confirm("[bold]Save changes?[/]")) return null;

        return new Server
        {
            Name     = name.Trim(),
            Host     = host.Trim(),
            Group    = group.Trim(),
            Protocol = protocol,
            Username = finalUsername,
            Password = finalPassword,
            Port     = finalPort,
            Tags     = finalTags,
        };
    }

    public static Server? Add()
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[bold steelblue1] Add Server[/]");
        AnsiConsole.MarkupLine("[dim]Name and Host are required. All other fields are optional.[/]");
        AnsiConsole.WriteLine();

        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Name[/] [dim](required)[/]")
                .Validate(v => !string.IsNullOrWhiteSpace(v)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Name is required")));

        var host = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Host[/] [dim](required)[/]")
                .Validate(v => !string.IsNullOrWhiteSpace(v)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Host is required")));

        var group = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Group[/]")
                .AllowEmpty());

        var protocol = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Protocol[/] [dim](rdp/ssh)[/]")
                .DefaultValue("rdp")
                .Validate(v => v is "rdp" or "ssh"
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be rdp or ssh[/]")));

        var username = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Username[/]")
                .AllowEmpty());

        var password = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Password[/] [dim](empty = none)[/]")
                .Secret()
                .AllowEmpty());

        var portStr = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Port[/] [dim](empty = default: 3389/22)[/]")
                .AllowEmpty());
        int? port = portStr.Length > 0 && int.TryParse(portStr, out var p) ? p : null;

        var tagsStr = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Tags[/] [dim](comma-separated, empty = none)[/]")
                .AllowEmpty());
        var tags = string.IsNullOrWhiteSpace(tagsStr)
            ? new List<string>()
            : [.. tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        AnsiConsole.WriteLine();
        if (!AnsiConsole.Confirm("[bold]Save new server?[/]")) return null;

        return new Server
        {
            Name     = name.Trim(),
            Host     = host.Trim(),
            Group    = group.Trim(),
            Protocol = protocol,
            Username = username.Length > 0 ? username.Trim() : null,
            Password = password.Length > 0 ? password : null,
            Port     = port,
            Tags     = tags,
        };
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>Optional field: shows current value in the hint, empty input = keep current.</summary>
    private static TextPrompt<string> OptionalField(string label, string? current)
    {
        var hint = current is { Length: > 0 } ? $"current: [white]{Markup.Escape(current)}[/], empty = keep" : "empty = none";
        return new TextPrompt<string>($"[bold]{label}[/] [dim]({hint})[/]")
            .AllowEmpty();
    }
}
