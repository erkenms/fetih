namespace Comet.Web.Features.Live;

using Microsoft.AspNetCore.SignalR;

public sealed class LiveHub(LiveWorldService live) : Hub
{
    public Task<LiveMapSnapshot> SubscribeMap(uint mapId) =>
        live.SubscribeMapAsync(this.Context.ConnectionId, mapId);

    public Task UnsubscribeMap() =>
        live.UnsubscribeMapAsync(this.Context.ConnectionId);

    public Task<OnlinePlayersResponse> SubscribeOnline() =>
        live.SubscribeOnlineAsync(this.Context.ConnectionId);

    public Task UnsubscribeOnline() =>
        live.UnsubscribeOnlineAsync(this.Context.ConnectionId);

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        live.Disconnect(this.Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
