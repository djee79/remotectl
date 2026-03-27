# RemoteCtl

A terminal-based remote connection manager for RDP and SSH, written in C# / .NET 10.

> Built with [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0). A self-contained publish bundles the runtime — no install needed on the target machine.

<img width="1536" height="1024" alt="RemoteCtl Terminal RDP Manager interface" src="https://github.com/user-attachments/assets/1f4d653e-d7d1-43af-859f-7fc23428cbb7" />

## Features

- Interactive fuzzy picker with live filtering by name, host, group, or tag
- Direct connect by name (`remotectl connect <name>`)
- RDP and SSH support with stored credentials
- AES-256-GCM encrypted password storage
- Connectivity check (`list --check`) with live port reachability
- WezTerm integration — open connections in a new tab (`--wezterm`)
- Cross-platform: Windows and Linux

## Dependencies

### Windows

| Purpose | Tool | Install |
|---|---|---|
| RDP | `mstsc.exe` | Built into Windows — no install needed |
| RDP (optional, full FreeRDP features) | `wfreerdp.exe` | `winget install FreeRDP.FreeRDP` |
| SSH with password | `plink.exe` (PuTTY) | `winget install PuTTY.PuTTY` |

> SSH without a stored password uses Windows built-in OpenSSH (`ssh.exe`) — no extra install needed.

### Linux

| Purpose | Tool | Install |
|---|---|---|
| RDP | `xfreerdp` / `xfreerdp3` | `sudo apt install freerdp3-x11` |
| SSH with password | `sshpass` | `sudo apt install sshpass` |

## Installation

### Build from source

```bash
dotnet build
```

### Publish self-contained (no .NET runtime required on target machine)

```bash
# Windows
dotnet publish -r win-x64 --self-contained

# Linux
dotnet publish -r linux-x64 --self-contained
```

Add the published binary to your PATH so you can run `remotectl` from anywhere.

## Configuration

Servers are stored in `servers.json`. Resolution order:

1. `$REMOTECTL_CONFIG` environment variable
2. `./servers.json` (current directory)
3. `~/.config/remotectl/servers.json`

### servers.json example

```json
[
  {
    "Name": "Web Server",
    "Host": "192.168.1.10",
    "Group": "Production / Web",
    "Protocol": "ssh",
    "Username": "admin",
    "Password": "enc:...",
    "Port": 22,
    "Tags": ["production", "critical"]
  },
  {
    "Name": "Office PC",
    "Host": "192.168.1.50",
    "Group": "Office",
    "Protocol": "rdp",
    "Username": "djee",
    "Password": "enc:..."
  }
]
```

### Field reference

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | Unique display name |
| `Host` | string | yes | IP address or hostname |
| `Group` | string | yes | Hierarchy path, e.g. `Plant A / Line 1` |
| `Protocol` | string | yes | `rdp` or `ssh` |
| `Tags` | string[] | no | Labels for filtering, e.g. `["production", "critical"]` |
| `Username` | string | no | Login username |
| `Password` | string | no | Plaintext (then run `encrypt`) or pre-encrypted `enc:…` |
| `Port` | int | no | Custom port — defaults to 3389 (RDP) or 22 (SSH) |

## Usage

```
remotectl                        # Open interactive server picker
remotectl connect <name>         # Connect directly by name
remotectl list                   # List all configured servers
remotectl list --check           # List with live port reachability check
remotectl search <term>          # Search by name, host, group, or tag
remotectl recent                 # Show last 20 connections
remotectl set-password <name>    # Securely set/update a server's password
remotectl encrypt                # Encrypt all plaintext passwords in servers.json
remotectl help                   # Show help
```

### Global flags

| Flag | Description |
|---|---|
| `--wezterm`, `-w` | Spawn the connection in a new WezTerm tab |
| `--config <path>` | Override the path to servers.json |

### Interactive picker

| Key | Action |
|---|---|
| `↑` / `↓` | Navigate the server list |
| `Enter` | Connect to the selected server |
| `Esc` | Exit without connecting |
| Any text | Filter by name, host, group, or tag (live) |
| `Backspace` | Delete last filter character |
| `#tagname` | Filter by tag — e.g. `#production` |
| `text #tag` | Combine text and tag filters — e.g. `plant #critical` |

## Password storage

Passwords are encrypted with **AES-256-GCM**. The encryption key is auto-generated on first use and stored at:

- **Linux:** `~/.config/remotectl/key` (chmod 600)
- **Windows:** same folder as `servers.json`

### Workflow

1. Add `"Password": "yourpassword"` to `servers.json`
2. Run `remotectl encrypt` — passwords become `enc:…` in the file
3. Or use `remotectl set-password <name>` for a hidden prompt (password never touches disk as plaintext)

> Keep the `key` file out of version control. `servers.json` is safe to commit once all passwords are encrypted.
