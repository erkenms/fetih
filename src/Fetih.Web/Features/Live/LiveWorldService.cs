namespace Fetih.Web.Features.Live;

using System.Collections.Concurrent;
using Fetih.Core.Logging;
using Fetih.Shared.Models.Admin;
using Fetih.Web.Features.Catalogs;
using Fetih.Web.Features.Items;
using Fetih.Web.Game;
using Microsoft.AspNetCore.SignalR;

public sealed class LiveWorldService(
    ILiveGameSource game,
    IHubContext<LiveHub> hub,
    CatalogStore catalogs,
    ItemCatalog items) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    public const string OnlineGroup = "online";

    private readonly Lock stateLock = new();
    private readonly Dictionary<string, uint> connectionMaps = new();
    private readonly Dictionary<uint, HashSet<string>> mapWatchers = new();
    private readonly HashSet<string> onlineWatchers = new();

    private readonly ConcurrentDictionary<uint, LiveMapSnapshot> lastSnapshots = new();
    private OnlinePlayersResponse? lastOnline;
    private bool lastGameOnline;

    public static string MapGroup(uint mapId) => $"map:{mapId}";

    public async Task<LiveMapSnapshot> SubscribeMapAsync(string connectionId, uint mapId)
    {
        uint? previousMap = null;
        lock (this.stateLock)
        {
            if (this.connectionMaps.TryGetValue(connectionId, out uint prev) && prev != mapId)
            {
                previousMap = prev;
                this.RemoveWatcherLocked(connectionId, prev);
            }

            this.connectionMaps[connectionId] = mapId;
            if (!this.mapWatchers.TryGetValue(mapId, out var watchers))
            {
                watchers = [];
                this.mapWatchers[mapId] = watchers;
            }
            watchers.Add(connectionId);
        }

        if (previousMap.HasValue)
            await hub.Groups.RemoveFromGroupAsync(connectionId, MapGroup(previousMap.Value));
        await hub.Groups.AddToGroupAsync(connectionId, MapGroup(mapId));

        if (this.lastSnapshots.TryGetValue(mapId, out var cached) && cached.GameOnline == game.Connected)
            return cached;

        var fresh = await this.FetchMapSnapshotAsync(mapId);
        if (fresh.GameOnline)
            this.lastSnapshots[mapId] = fresh;
        return fresh;
    }

    public async Task UnsubscribeMapAsync(string connectionId)
    {
        uint? previousMap = null;
        lock (this.stateLock)
        {
            if (this.connectionMaps.Remove(connectionId, out uint prev))
            {
                previousMap = prev;
                this.RemoveWatcherLocked(connectionId, prev);
            }
        }

        if (previousMap.HasValue)
            await hub.Groups.RemoveFromGroupAsync(connectionId, MapGroup(previousMap.Value));
    }

    public async Task<OnlinePlayersResponse> SubscribeOnlineAsync(string connectionId)
    {
        lock (this.stateLock)
            this.onlineWatchers.Add(connectionId);

        await hub.Groups.AddToGroupAsync(connectionId, OnlineGroup);

        if (this.lastOnline is { } cached && cached.GameOnline == game.Connected)
            return cached;

        var fresh = await this.FetchOnlinePlayersAsync();
        if (fresh.GameOnline)
            this.lastOnline = fresh;
        return fresh;
    }

    public async Task UnsubscribeOnlineAsync(string connectionId)
    {
        lock (this.stateLock)
        {
            this.onlineWatchers.Remove(connectionId);
            if (this.onlineWatchers.Count == 0)
                this.lastOnline = null;
        }

        await hub.Groups.RemoveFromGroupAsync(connectionId, OnlineGroup);
    }

    public void Disconnect(string connectionId)
    {
        lock (this.stateLock)
        {
            if (this.connectionMaps.Remove(connectionId, out uint prev))
                this.RemoveWatcherLocked(connectionId, prev);

            this.onlineWatchers.Remove(connectionId);
            if (this.onlineWatchers.Count == 0)
                this.lastOnline = null;
        }
    }

    public async Task<OnlinePlayersResponse> FetchOnlinePlayersAsync()
    {
        if (!game.Connected)
            return new OnlinePlayersResponse(false, DateTime.UtcNow, []);

        try
        {
            var raw = await game.OnlinePlayersAsync();
            var players = raw
                .Select(p => new LiveOnlinePlayer(
                    p.CharacterID,
                    p.AccountID,
                    p.Name,
                    p.Level,
                    p.Class,
                    p.Rebirths,
                    p.MapID,
                    this.MapNameOf(p.MapID),
                    p.X,
                    p.Y,
                    p.PkPoints,
                    p.Authority,
                    p.IPAddress,
                    p.LoggedInAt))
                .ToArray();
            return new OnlinePlayersResponse(true, DateTime.UtcNow, players);
        }
        catch (GameOfflineException)
        {
            return new OnlinePlayersResponse(false, DateTime.UtcNow, []);
        }
    }

    public async Task<LiveMapSnapshot> FetchMapSnapshotAsync(uint mapId)
    {
        if (!game.Connected)
            return OfflineSnapshot(mapId);

        try
        {
            var state = await game.MapSnapshotAsync(mapId);
            if (state == null)
            {
                return new LiveMapSnapshot(
                    mapId, 0, 0, DateTime.UtcNow,
                    GameOnline: true, Exists: false,
                    [], [], [], []);
            }

            var groundItems = state.GroundItems
                .Select(i =>
                {
                    CatalogItem? catalogItem = null;
                    if (i.Silver == 0)
                        items.TryGet(i.TypeID, out catalogItem);
                    return new LiveGroundItem(
                        i.ID,
                        i.TypeID,
                        catalogItem?.Name,
                        catalogItem?.GroundIcon ?? catalogItem?.Icon,
                        i.X,
                        i.Y,
                        i.Silver,
                        i.ExpiresAt);
                })
                .ToArray();

            return new LiveMapSnapshot(
                state.MapID,
                state.Width,
                state.Height,
                state.TakenAt,
                GameOnline: true,
                Exists: true,
                [.. state.Players],
                [.. state.Monsters],
                groundItems,
                [.. state.Booths]);
        }
        catch (GameOfflineException)
        {
            return OfflineSnapshot(mapId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // A failed poll mustn't end the service: the host stops when a background service throws
            try
            {
                await this.PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                LogEvents.For<LiveWorldService>(WebEvents.Error).Error(e, "Live world poll failed");
            }
        }
    }

    internal async Task PollOnceAsync(CancellationToken ct = default)
    {
        uint[] watchedMaps;
        bool watchOnline;
        lock (this.stateLock)
        {
            watchedMaps = [.. this.mapWatchers.Keys];
            watchOnline = this.onlineWatchers.Count > 0;
        }

        if (watchedMaps.Length == 0 && !watchOnline)
            return;

        if (!game.Connected)
        {
            if (this.lastGameOnline)
            {
                this.lastGameOnline = false;
                this.lastSnapshots.Clear();
                this.lastOnline = null;
                await this.NotifyGameStatusAsync(watchedMaps, watchOnline, false, ct);
            }
            return;
        }

        bool justReconnected = !this.lastGameOnline;
        this.lastGameOnline = true;
        if (justReconnected)
            await this.NotifyGameStatusAsync(watchedMaps, watchOnline, true, ct);

        foreach (uint mapId in watchedMaps)
        {
            var next = await this.FetchMapSnapshotAsync(mapId);
            if (!next.GameOnline)
            {
                this.lastGameOnline = false;
                this.lastSnapshots.Clear();
                this.lastOnline = null;
                await this.NotifyGameStatusAsync(watchedMaps, watchOnline, false, ct);
                return;
            }

            if (!this.lastSnapshots.TryGetValue(mapId, out var prev) || !prev.GameOnline || prev.Exists != next.Exists)
            {
                this.lastSnapshots[mapId] = next;
                await hub.Clients.Group(MapGroup(mapId)).SendAsync("Snapshot", next, ct);
                continue;
            }

            var delta = LiveDeltaCalculator.ComputeDelta(prev, next);
            this.lastSnapshots[mapId] = next;
            if (!delta.IsEmpty)
                await hub.Clients.Group(MapGroup(mapId)).SendAsync("Delta", delta, ct);
        }

        if (watchOnline)
        {
            var online = await this.FetchOnlinePlayersAsync();
            if (!online.GameOnline)
            {
                this.lastGameOnline = false;
                this.lastOnline = null;
                await this.NotifyGameStatusAsync(watchedMaps, watchOnline, false, ct);
                return;
            }

            if (this.lastOnline == null ||
                !this.lastOnline.GameOnline ||
                LiveDeltaCalculator.OnlinePlayersChanged(this.lastOnline.Players, online.Players))
            {
                this.lastOnline = online;
                await hub.Clients.Group(OnlineGroup).SendAsync("OnlinePlayers", online, ct);
            }
        }
    }

    private async Task NotifyGameStatusAsync(uint[] watchedMaps, bool watchOnline, bool online, CancellationToken ct)
    {
        var tasks = new List<Task>(watchedMaps.Length + (watchOnline ? 1 : 0));
        foreach (uint mapId in watchedMaps)
            tasks.Add(hub.Clients.Group(MapGroup(mapId)).SendAsync("GameStatus", online, ct));
        if (watchOnline)
            tasks.Add(hub.Clients.Group(OnlineGroup).SendAsync("GameStatus", online, ct));
        await Task.WhenAll(tasks);
    }

    private void RemoveWatcherLocked(string connectionId, uint mapId)
    {
        if (!this.mapWatchers.TryGetValue(mapId, out var watchers))
            return;
        watchers.Remove(connectionId);
        if (watchers.Count == 0)
        {
            this.mapWatchers.Remove(mapId);
            this.lastSnapshots.TryRemove(mapId, out _);
        }
    }

    private string? MapNameOf(uint mapId) =>
        catalogs.MapsById.TryGetValue(mapId, out var entry) ? entry.Name : null;

    private static LiveMapSnapshot OfflineSnapshot(uint mapId) => new(
        mapId, 0, 0, DateTime.UtcNow,
        GameOnline: false, Exists: false,
        [], [], [], []);
}
