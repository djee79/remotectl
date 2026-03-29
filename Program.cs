using System.Text;
using RemoteCtl.CLI;

Console.OutputEncoding = Encoding.UTF8;

// Support: remotectl --config /path/to/servers.json [command...]
string? configPath = null;
var effectiveArgs = args;

if (args.Length >= 2 && args[0] == "--config")
{
    configPath = args[1];
    effectiveArgs = args[2..];
}

var handler = new CommandHandler(configPath);
await handler.RunAsync(effectiveArgs);
