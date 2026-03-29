using System.Text;
using RemoteCtl.Models;
using Spectre.Console;

namespace RemoteCtl.UI;

public enum MenuAction { Connect, Edit, Add, Delete, Cancel }

public class MenuRenderer
{
    private const int PageSize = 20;

    public (Server? server, bool useMultimon, MenuAction action) Show(IReadOnlyList<Server> servers)
    {
        if (servers.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No servers configured.[/]  [dim]Press N to add one.[/]");
            var k = Console.ReadKey(intercept: true);
            if (k.KeyChar is 'n' or 'N') return (null, false, MenuAction.Add);
            return (null, false, MenuAction.Cancel);
        }

        var filter       = new StringBuilder();
        var selectedIndex = 0;
        var viewOffset   = 0;
        var useMultimon  = false;
        var searchMode   = false;   // false = normal (vim), true = search/insert

        Console.CursorVisible = false;

        try
        {
            while (true)
            {
                var filterStr = filter.ToString();
                var filtered  = ApplyFilter(servers, filterStr);

                selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, filtered.Count - 1));

                if (selectedIndex < viewOffset)
                    viewOffset = selectedIndex;
                if (selectedIndex >= viewOffset + PageSize)
                    viewOffset = selectedIndex - PageSize + 1;

                Render(filterStr, filtered, selectedIndex, viewOffset, useMultimon, searchMode);

                var key = Console.ReadKey(intercept: true);

                // ── Search / insert mode ──────────────────────────────────────
                if (searchMode)
                {
                    switch (key.Key)
                    {
                        case ConsoleKey.Escape:
                            filter.Clear();
                            selectedIndex = 0;
                            viewOffset    = 0;
                            searchMode    = false;
                            break;

                        case ConsoleKey.Enter:
                            searchMode = false;  // confirm filter, back to normal
                            break;

                        case ConsoleKey.Backspace:
                            if (filter.Length > 0)
                            {
                                filter.Remove(filter.Length - 1, 1);
                                selectedIndex = 0;
                                viewOffset    = 0;
                            }
                            break;

                        default:
                            if (!char.IsControl(key.KeyChar))
                            {
                                filter.Append(key.KeyChar);
                                selectedIndex = 0;
                                viewOffset    = 0;
                            }
                            break;
                    }
                    continue;
                }

                // ── Normal mode (vim) ─────────────────────────────────────────
                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                        return (null, false, MenuAction.Cancel);

                    case ConsoleKey.Enter:
                        if (filtered.Count > 0)
                            return (filtered[selectedIndex], useMultimon, MenuAction.Connect);
                        break;

                    case ConsoleKey.UpArrow:
                        if (selectedIndex > 0) selectedIndex--;
                        break;

                    case ConsoleKey.DownArrow:
                        if (selectedIndex < filtered.Count - 1) selectedIndex++;
                        break;

                    case ConsoleKey.Delete:
                        if (filtered.Count > 0)
                            return (filtered[selectedIndex], false, MenuAction.Delete);
                        break;

                    default:
                        switch (key.KeyChar)
                        {
                            // ── Movement ──────────────────────────────────────
                            case 'k' or 'K':
                                if (selectedIndex > 0) selectedIndex--;
                                break;
                            case 'j' or 'J':
                                if (selectedIndex < filtered.Count - 1) selectedIndex++;
                                break;

                            // ── Actions ───────────────────────────────────────
                            case 'l' or 'L' or 'o' or 'O':
                                if (filtered.Count > 0)
                                    return (filtered[selectedIndex], useMultimon, MenuAction.Connect);
                                break;
                            case 'h' or 'H':
                                return (null, false, MenuAction.Cancel);
                            case 'e' or 'E':
                                if (filtered.Count > 0)
                                    return (filtered[selectedIndex], false, MenuAction.Edit);
                                break;
                            case 'n' or 'N':
                                return (null, false, MenuAction.Add);
                            case 'd' or 'D':
                                if (filtered.Count > 0)
                                    return (filtered[selectedIndex], false, MenuAction.Delete);
                                break;
                            case 'm' or 'M':
                                useMultimon = !useMultimon;
                                break;

                            // ── Enter search mode ─────────────────────────────
                            case '/':
                                searchMode = true;
                                break;
                        }
                        break;
                }
            }
        }
        finally
        {
            Console.CursorVisible = true;
            Console.Write("\x1b[2J\x1b[H");
        }
    }

    private static (List<string> textTerms, List<string> tagTerms) ParseFilter(string filter)
    {
        var tokens = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var text   = tokens.Where(t => !t.StartsWith('#')).ToList();
        var tags   = tokens.Where(t => t.StartsWith('#') && t.Length > 1)
                           .Select(t => t[1..])
                           .ToList();
        return (text, tags);
    }

    private static List<Server> ApplyFilter(IReadOnlyList<Server> servers, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return [.. servers];

        var (textTerms, tagTerms) = ParseFilter(filter);

        return [.. servers.Where(s =>
            // All text terms must match name/host/group
            textTerms.All(t =>
                s.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                s.Host.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                s.Group.Contains(t, StringComparison.OrdinalIgnoreCase)) &&
            // All #tag terms must match at least one of the server's tags
            tagTerms.All(tag =>
                s.Tags.Any(st => st.Contains(tag, StringComparison.OrdinalIgnoreCase))))];
    }

    private static void Render(string filter, List<Server> filtered, int selectedIndex, int viewOffset, bool useMultimon, bool searchMode)
    {
        Console.Write("\x1b[2J\x1b[H");

        var (_, tagTerms) = ParseFilter(filter);
        var hasTagFilter  = tagTerms.Count > 0;

        // Header
        var multimonBadge = useMultimon ? " [bold yellow]⬛⬛ multimon[/]" : "";
        AnsiConsole.MarkupLine($"[bold steelblue1] RemoteCtl [/] [dim]Terminal RDP Manager[/]{multimonBadge}");
        RenderSearchBar(filter, tagTerms, searchMode);

        if (searchMode)
            AnsiConsole.MarkupLine("[dim]  type to filter  ·  [white]#tag[/] for tags  ·  Enter confirm  ·  Esc clear & exit search[/]");
        else
            AnsiConsole.MarkupLine($"[dim]  {filtered.Count} server(s)  ·  j/k move  ·  l/o open  ·  e edit  ·  n new  ·  d delete  ·  m multimon  ·  / search  ·  h/Esc exit[/]");

        AnsiConsole.MarkupLine("[dim]" + new string('─', 64) + "[/]");

        if (filtered.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim yellow]  No servers match your query.[/]");
            AnsiConsole.MarkupLine("[dim]  Tip: use [white]#tagname[/] to filter by tag  ·  combine: [white]plant #critical[/]  ·  Esc to clear[/]");
            return;
        }

        var page = filtered.Skip(viewOffset).Take(PageSize).ToList();

        for (int i = 0; i < page.Count; i++)
        {
            var s          = page[i];
            var absIdx     = viewOffset + i;
            var isSelected = absIdx == selectedIndex;

            var isSsh = s.Protocol.Equals("ssh", StringComparison.OrdinalIgnoreCase);
            // RDP = orange family,  SSH = green family  (clearly distinct)
            var color = isSsh ? "green3" : "darkorange";
            var badge = isSsh ? $"[{color}]ssh[/]" : $"[{color}]rdp[/]";

            var group = Markup.Escape(TruncatePad(s.Group, 20));
            var name  = Markup.Escape(TruncatePad(s.Name, 22));
            var host  = Markup.Escape(s.Host);
            var tags  = RenderTags(s.Tags, tagTerms, isSelected);

            if (isSelected)
                AnsiConsole.MarkupLine(
                    $"[bold green]▶[/] {badge} [bold {color}]{group}[/] [dim]│[/] [bold {color}]{name}[/] [dim]([yellow]{host}[/])[/]{tags}");
            else
                AnsiConsole.MarkupLine(
                    $"  {badge} [dim {color}]{group}[/] [dim]│[/] [{color}]{name}[/] [dim]({host})[/]{tags}");
        }

        var remaining = filtered.Count - viewOffset - PageSize;
        if (remaining > 0)
            AnsiConsole.MarkupLine($"[dim]  ··· {remaining} more — keep typing to narrow[/]");

        if (!hasTagFilter && !searchMode)
            AnsiConsole.MarkupLine("[dim]  Tip: press [white]/[/] then [white]#tagname[/] to filter by tag[/]");
    }

    private static void RenderSearchBar(string filter, List<string> tagTerms, bool searchMode)
    {
        // In normal mode with no filter, show a dim prompt
        if (!searchMode && filter.Length == 0)
        {
            AnsiConsole.MarkupLine("[dim]  / to search[/]");
            return;
        }

        var modeLabel = searchMode
            ? "[bold yellow] SEARCH [/][dim] >[/]"
            : "[dim] FILTER >[/]";

        if (tagTerms.Count == 0)
        {
            var cursor = searchMode ? "[bold white]█[/]" : "";
            AnsiConsole.MarkupLine($"{modeLabel} [white]{Markup.Escape(filter)}[/]{cursor}");
            return;
        }

        // Render text and tag tokens with distinct colors
        var tokens = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder($"{modeLabel} ");
        var lastWasToken = false;

        foreach (var token in tokens)
        {
            if (lastWasToken) sb.Append("[dim] [/]");
            sb.Append(token.StartsWith('#')
                ? $"[bold magenta]{Markup.Escape(token)}[/]"
                : $"[white]{Markup.Escape(token)}[/]");
            lastWasToken = true;
        }

        if (searchMode)
        {
            if (filter.EndsWith(' '))
                sb.Append("[dim] [/][bold white]█[/]");
            else
                sb.Append("[bold white]█[/]");
        }

        AnsiConsole.MarkupLine(sb.ToString());
    }

    private static string RenderTags(List<string> tags, List<string> activeTagTerms, bool isSelected)
    {
        if (tags.Count == 0) return "";

        var parts = tags.Select(t =>
        {
            var isActive = activeTagTerms.Any(f => t.Contains(f, StringComparison.OrdinalIgnoreCase));
            return isActive
                ? $"[bold magenta]#{Markup.Escape(t)}[/]"
                : (isSelected ? $"[dim]#{Markup.Escape(t)}[/]" : $"[dim]#{Markup.Escape(t)}[/]");
        });

        return "  " + string.Join(" ", parts);
    }

    private static string TruncatePad(string s, int width)
    {
        if (s.Length > width) return s[..(width - 1)] + "…";
        return s.PadRight(width);
    }
}
