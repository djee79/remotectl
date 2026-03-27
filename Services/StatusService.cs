using System.Net.Sockets;

namespace RemoteCtl.Services;

public class StatusService
{
    public async Task<bool> IsReachableAsync(string host, int port = 3389, int timeoutMs = 1500)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            return client.Connected;
        }
        catch { return false; }
    }

    public async Task<Dictionary<string, bool>> CheckAllAsync(
        IEnumerable<string> hosts,
        int port = 3389,
        int timeoutMs = 1500)
    {
        var tasks = hosts.Distinct().Select(async h => (h, await IsReachableAsync(h, port, timeoutMs)));
        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(r => r.h, r => r.Item2);
    }
}
