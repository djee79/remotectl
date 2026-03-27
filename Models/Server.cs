namespace RemoteCtl.Models;

public class Server
{
    public string Name     { get; set; } = "";
    public string Host     { get; set; } = "";
    public string Group    { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int?    Port     { get; set; }   // null = use protocol default (RDP:3389, SSH:22)
    public string Protocol { get; set; } = "rdp";
}
