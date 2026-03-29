using System.Diagnostics;
using System.Runtime.InteropServices;
using RemoteCtl.Models;
using Spectre.Console;

namespace RemoteCtl.Services;

public class ConnectionService
{
    private readonly RecentRepository _recent;

    public ConnectionService(RecentRepository recent) => _recent = recent;

    public void Connect(Server server, bool useWezterm = false, bool useMultimon = false)
    {
        _recent.Add(server.Name);

        var isSsh = server.Protocol.Equals("ssh", StringComparison.OrdinalIgnoreCase);
        var (exe, args, waitForExit) = isSsh
            ? BuildSsh(server, useWezterm)
            : BuildRdp(server, useWezterm, useMultimon);

        AnsiConsole.MarkupLine($"\n[green]Connecting[/] → [bold]{Markup.Escape(server.Name)}[/] [dim]({Markup.Escape(server.Host)})[/]");
        if (useWezterm)
            AnsiConsole.MarkupLine("[dim steelblue1]  via WezTerm (new tab)[/]");
        AnsiConsole.MarkupLine($"[dim]$ {Markup.Escape(exe)} {Markup.Escape(args)}[/]\n");

        try
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false };
            var process = Process.Start(psi);
            if (waitForExit) process?.WaitForExit();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to launch:[/] {Markup.Escape(ex.Message)}");
            var hint = (isSsh, useWezterm) switch
            {
                (true,  true)  => "Make sure WezTerm is running and 'wezterm' is in PATH.",
                (true,  false) => "Make sure 'ssh' is installed and in PATH.",
                (false, true)  => "Make sure WezTerm is running and 'wezterm' is in PATH.",
                (false, false) => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "Make sure wfreerdp is installed and in PATH."
                    : "Make sure xfreerdp is installed and in PATH.",
            };
            AnsiConsole.MarkupLine($"[dim]{hint}[/]");
        }
    }

    // ── SSH ───────────────────────────────────────────────────────────────────

    private static (string exe, string args, bool waitForExit) BuildSsh(Server server, bool useWezterm)
    {
        var (exe, sshArgs) = BuildSshExeAndArgs(server);

        if (useWezterm)
        {
            var title = EscapeTitle(server.Name);
            return ("wezterm", $"cli spawn --title \"{title}\" -- {exe} {sshArgs}", false);
        }

        return (exe, sshArgs, true);
    }

    private static (string exe, string args) BuildSshExeAndArgs(Server server)
    {
        if (server.Password is { Length: > 0 })
        {
            // On Windows, plink (PuTTY) natively supports -pw without PTY tricks
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsOnPath("plink.exe"))
                return ("plink", BuildPlinkArgs(server));

            // On Linux/Mac, sshpass wraps ssh using a PTY to inject the password
            var sshpass = ResolveSshpass();
            if (sshpass is not null)
                return (sshpass, $"-p {server.Password} ssh {BuildSshArgs(server)}");
        }

        return ("ssh", BuildSshArgs(server));
    }

    private static string BuildPlinkArgs(Server server)
    {
        var parts = new List<string>();

        if (server.Password is { Length: > 0 } pw)
            parts.Add($"-pw \"{pw}\"");

        if (server.Port is int port)
            parts.Add($"-P {port}");

        if (server.Username is { Length: > 0 } u)
            parts.Add($"-l {u}");

        parts.Add(server.Host);
        return string.Join(" ", parts);
    }

    private static string BuildSshArgs(Server server)
    {
        var parts = new List<string>();

        if (server.Port is int port)
            parts.Add($"-p {port}");

        // Suppress host key checking prompts for non-interactive use
        parts.Add("-o StrictHostKeyChecking=accept-new");

        var target = server.Username is { Length: > 0 } u
            ? $"{u}@{server.Host}"
            : server.Host;

        parts.Add(target);
        return string.Join(" ", parts);
    }

    private static string? ResolveSshpass()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;

        // Check for bundled sshpass shipped alongside this executable
        var rid = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "linux";
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            _                  => "x64",
        };
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "sshpass", $"{rid}-{arch}", "sshpass");
        if (File.Exists(bundled)) return bundled;

        if (IsOnPath("sshpass")) return "sshpass";

        return null;
    }

    private static bool IsOnPath(string exe)
    {
        var (cmd, arg) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("where", exe.EndsWith(".exe") ? exe : exe + ".exe")
            : ("which", exe);
        try
        {
            var p = Process.Start(new ProcessStartInfo(cmd, arg)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            p?.WaitForExit();
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    // ── RDP ───────────────────────────────────────────────────────────────────

    private static (string exe, string args, bool waitForExit) BuildRdp(Server server, bool useWezterm, bool useMultimon = false)
    {
        var rdpExe  = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ResolveWindowsRdpExe() : ResolveFreerdp();
        var isMstsc = rdpExe.Equals("mstsc.exe", StringComparison.OrdinalIgnoreCase);
        var rdpArgs = isMstsc ? BuildMstscArgs(server) : BuildFreerdpArgs(server, useMultimon);

        if (useWezterm)
        {
            var title = EscapeTitle(server.Name);
            return ("wezterm", $"cli spawn --title \"{title}\" -- {rdpExe} {rdpArgs}", false);
        }

        return (rdpExe, rdpArgs, false);
    }

    // mstsc.exe only supports /v: and a few basic flags; credentials go via Windows credential manager
    private static string BuildMstscArgs(Server server)
    {
        var host  = server.Port is int p ? $"{server.Host}:{p}" : server.Host;
        return $"/v:{host} /f";
    }

    private static string BuildFreerdpArgs(Server server, bool useMultimon = false)
    {
        var host  = server.Port is int p ? $"{server.Host}:{p}" : server.Host;
        var parts = new List<string> { $"/v:{host}", useMultimon ? "/multimon" : "/f", "/cert:ignore" };

        if (server.Username is { Length: > 0 } u)
            parts.Add($"/u:{u}");

        if (server.Password is { Length: > 0 } pw)
            parts.Add($"/p:{pw}");
        else
            parts.Add("/sec:tls");

        return string.Join(" ", parts);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ResolveWindowsRdpExe()
    {
        // Prefer bundled wfreerdp shipped alongside this executable
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "freerdp", "win64", "wfreerdp.exe");
        if (File.Exists(bundled)) return bundled;

        // Fall back to a system-wide FreeRDP install
        var installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "FreeRDP", "wfreerdp.exe");
        if (File.Exists(installed)) return installed;

        if (IsOnPath("wfreerdp.exe")) return "wfreerdp.exe";

        return "mstsc.exe"; // always present on Windows
    }

    private static string ResolveFreerdp()
    {
        foreach (var candidate in new[] { "xfreerdp3", "xfreerdp" })
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("which", candidate)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                });
                p?.WaitForExit();
                if (p?.ExitCode == 0) return candidate;
            }
            catch { }
        }
        return "xfreerdp";
    }

    private static string EscapeTitle(string name) => name.Replace("\"", "\\\"");
}
