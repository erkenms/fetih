namespace Comet.Web.Features.PublicHeatmap;

using Comet.Shared.Models.Admin;
using Comet.Web.Game;

public interface IHeatmapGameSource
{
    bool Connected { get; }

    Task<List<OnlinePlayer>> OnlinePlayersAsync();
}

public sealed class RpcHeatmapGameSource(GameRpc rpc) : IHeatmapGameSource
{
    public bool Connected => rpc.Connected;

    public Task<List<OnlinePlayer>> OnlinePlayersAsync() =>
        rpc.CallAsync<List<OnlinePlayer>>("OnlinePlayers");
}
