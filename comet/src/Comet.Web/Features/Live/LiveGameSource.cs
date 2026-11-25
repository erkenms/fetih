namespace Comet.Web.Features.Live;

using Comet.Shared.Models.Admin;
using Comet.Web.Game;

public interface ILiveGameSource
{
    bool Connected { get; }

    Task<List<OnlinePlayer>> OnlinePlayersAsync();

    Task<MapState?> MapSnapshotAsync(uint mapId);
}

public sealed class RpcLiveGameSource(GameRpc rpc) : ILiveGameSource
{
    public bool Connected => rpc.Connected;

    public Task<List<OnlinePlayer>> OnlinePlayersAsync() =>
        rpc.CallAsync<List<OnlinePlayer>>("OnlinePlayers");

    public Task<MapState?> MapSnapshotAsync(uint mapId) =>
        rpc.CallAsync<MapState?>("MapSnapshot", mapId);
}
