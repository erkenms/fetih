namespace Comet.Web.Game;

using System.Net.Sockets;
using Comet.Core.Logging;
using StreamJsonRpc;

public class GameOfflineException(string message = "The game server isn't connected")
    : Exception(message);

// The web's connection to the game server's RPC port. It reconnects on its own; a call while
// the game is down throws GameOfflineException, which the API answers with 503 game_offline.
public sealed class GameRpc(WebConfiguration config) : BackgroundService
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private volatile JsonRpc? Rpc;

    public bool Connected => this.Rpc is { } rpc && !rpc.Completion.IsCompleted;

    public async Task<T> CallAsync<T>(string method, params object[] args)
    {
        var rpc = this.Rpc;
        if (rpc == null || rpc.Completion.IsCompleted)
            throw new GameOfflineException();
        using var timeout = new CancellationTokenSource(CallTimeout);
        try
        {
            return await rpc.InvokeWithCancellationAsync<T>(method, args, timeout.Token);
        }
        catch (ConnectionLostException) { throw new GameOfflineException(); }
        catch (ObjectDisposedException) { throw new GameOfflineException(); }
    }

    public Task CallAsync(string method, params object[] args) =>
        this.CallAsync<object>(method, args);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool wasConnected = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(config.GameRpc.Hostname, config.GameRpc.Port, stoppingToken);
                await using var stream = client.GetStream();
                using var rpc = JsonRpc.Attach(stream);
                await rpc.InvokeWithCancellationAsync("Connected", ["Web"], stoppingToken);
                this.Rpc = rpc;
                wasConnected = true;
                LogEvents.For<GameRpc>(WebEvents.GameRpcConnected).Information(
                    "Connected to the game server's RPC at {Host}:{Port}",
                    config.GameRpc.Hostname, config.GameRpc.Port);
                await rpc.Completion.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) when (e is SocketException or IOException or ConnectionLostException
                or RemoteInvocationException) { }

            this.Rpc = null;
            if (wasConnected)
            {
                wasConnected = false;
                LogEvents.For<GameRpc>(WebEvents.GameRpcLost).Warning(
                    "Lost the game server's RPC connection; retrying");
            }

            try { await Task.Delay(RetryDelay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
