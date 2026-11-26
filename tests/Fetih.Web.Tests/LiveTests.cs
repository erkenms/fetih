#nullable enable
namespace Fetih.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fetih.Shared.Models.Admin;
using Fetih.Web.Features.Catalogs;
using Fetih.Web.Features.Items;
using Fetih.Web.Features.Live;
using Microsoft.AspNetCore.SignalR;
using Xunit;

[Collection(WebCollection.Name)]
public class LiveTests(WebFixture fixture)
{
    [Fact]
    public async Task PlayerIsRefused()
    {
        var client = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/live/online")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/live/maps/1002")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/hubs/live/negotiate?negotiateVersion=1", null)).StatusCode);
    }

    [Fact]
    public async Task ModeratorSeesOfflineStateWhenGameIsDown()
    {
        var client = await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");

        var online = await client.GetFromJsonAsync<JsonElement>("/api/live/online");
        Assert.False(online.GetProperty("gameOnline").GetBoolean());
        Assert.Empty(online.GetProperty("players").EnumerateArray());

        var map = await client.GetFromJsonAsync<JsonElement>("/api/live/maps/1002");
        Assert.False(map.GetProperty("gameOnline").GetBoolean());
        Assert.Equal(1002u, map.GetProperty("mapId").GetUInt32());
        Assert.Empty(map.GetProperty("players").EnumerateArray());

        var negotiate = await client.PostAsync("/hubs/live/negotiate?negotiateVersion=1", null);
        negotiate.EnsureSuccessStatusCode();
    }

    [Fact]
    public void DeltaCalculatorDetectsChangesAndRemovals()
    {
        var now = DateTime.UtcNow;
        var prev = new LiveMapSnapshot(
            1002, 1000, 1000, now, true, true,
            [
                new MapPlayer { CharacterID = 1, Name = "Alice", Level = 50, X = 400, Y = 400, NameColor = "white", Health = 100, MaxHealth = 100 },
                new MapPlayer { CharacterID = 2, Name = "Bob", Level = 60, X = 410, Y = 410, NameColor = "white", Health = 200, MaxHealth = 200 },
            ],
            [
                new MapMonster { ID = 10, TypeID = 1, Name = "Pheasant", Level = 1, X = 300, Y = 300, Alive = true, Health = 50, MaxHealth = 50 },
            ],
            [
                new LiveGroundItem(100, 1088001, "Meteor", null, 305, 305, 0, now.AddMinutes(1)),
            ],
            [
                new MapBooth { ID = 500, OwnerID = 1, OwnerName = "Alice", X = 401, Y = 401, Items = 2, Hawk = "WTS" },
            ]);

        var same = LiveDeltaCalculator.ComputeDelta(prev, prev);
        Assert.True(same.IsEmpty);

        var next = new LiveMapSnapshot(
            1002, 1000, 1000, now.AddSeconds(1), true, true,
            [
                // Alice moved
                new MapPlayer { CharacterID = 1, Name = "Alice", Level = 50, X = 405, Y = 402, NameColor = "white", Health = 100, MaxHealth = 100 },
                // Bob left, Charlie joined
                new MapPlayer { CharacterID = 3, Name = "Charlie", Level = 70, X = 420, Y = 420, NameColor = "red", Health = 300, MaxHealth = 300 },
            ],
            [
                // Pheasant took damage
                new MapMonster { ID = 10, TypeID = 1, Name = "Pheasant", Level = 1, X = 300, Y = 300, Alive = true, Health = 25, MaxHealth = 50 },
            ],
            [
                // Meteor picked up, silver dropped
                new LiveGroundItem(101, 1090000, null, null, 310, 310, 500, now.AddMinutes(1)),
            ],
            []);

        var delta = LiveDeltaCalculator.ComputeDelta(prev, next);
        Assert.False(delta.IsEmpty);
        Assert.Equal([1u, 3u], delta.PlayersUpsert.Select(p => p.CharacterID).Order().ToArray());
        Assert.Equal([2u], delta.PlayersRemoved);
        Assert.Single(delta.MonstersUpsert);
        Assert.Equal(25u, delta.MonstersUpsert[0].Health);
        Assert.Empty(delta.MonstersRemoved);
        Assert.Single(delta.GroundItemsUpsert);
        Assert.Equal(101u, delta.GroundItemsUpsert[0].Id);
        Assert.Equal([100u], delta.GroundItemsRemoved);
        Assert.Empty(delta.BoothsUpsert);
        Assert.Equal([500u], delta.BoothsRemoved);
    }

    [Fact]
    public async Task LiveWorldServicePollsOnlyWhileWatchedAndSendsDeltas()
    {
        var source = new FakeLiveGameSource();
        var hub = new FakeHubContext();
        var service = new LiveWorldService(source, hub, new CatalogStore(new WebConfiguration()), new ItemCatalog((string?)null));

        // Nobody watching -> zero RPC calls
        await service.PollOnceAsync();
        Assert.Equal(0, source.MapCalls);
        Assert.Equal(0, source.OnlineCalls);

        // Subscribe to map 1002
        source.Maps[1002] = new MapState
        {
            MapID = 1002, Width = 1000, Height = 1000, TakenAt = DateTime.UtcNow,
            Players = [new MapPlayer { CharacterID = 1, Name = "Hero", Level = 10, X = 430, Y = 378, NameColor = "white" }],
        };
        var initial = await service.SubscribeMapAsync("conn-1", 1002);
        Assert.True(initial.GameOnline);
        Assert.Single(initial.Players);
        Assert.Equal(1, source.MapCalls);

        // Poll with no change -> no delta broadcast
        hub.Messages.Clear();
        await service.PollOnceAsync();
        Assert.DoesNotContain(hub.Messages, m => m.Method == "Delta");

        // Move player -> delta broadcast with updated player
        source.Maps[1002] = new MapState
        {
            MapID = 1002, Width = 1000, Height = 1000, TakenAt = DateTime.UtcNow,
            Players = [new MapPlayer { CharacterID = 1, Name = "Hero", Level = 10, X = 435, Y = 380, NameColor = "white" }],
        };
        hub.Messages.Clear();
        await service.PollOnceAsync();
        var deltaMsg = Assert.Single(hub.Messages, m => m.Method == "Delta");
        Assert.Equal("map:1002", deltaMsg.Group);
        var delta = Assert.IsType<LiveMapDelta>(deltaMsg.Arg);
        Assert.Single(delta.PlayersUpsert);
        Assert.Equal((ushort)435, delta.PlayersUpsert[0].X);

        // Disconnect -> stops polling map 1002
        service.Disconnect("conn-1");
        int callsBefore = source.MapCalls;
        await service.PollOnceAsync();
        Assert.Equal(callsBefore, source.MapCalls);
    }

    private sealed class FakeLiveGameSource : ILiveGameSource
    {
        public bool Connected { get; set; } = true;
        public int MapCalls { get; private set; }
        public int OnlineCalls { get; private set; }
        public List<OnlinePlayer> Online { get; set; } = [];
        public Dictionary<uint, MapState> Maps { get; } = new();

        public Task<List<OnlinePlayer>> OnlinePlayersAsync()
        {
            this.OnlineCalls++;
            return Task.FromResult(this.Online);
        }

        public Task<MapState?> MapSnapshotAsync(uint mapId)
        {
            this.MapCalls++;
            return Task.FromResult(this.Maps.TryGetValue(mapId, out var state) ? state : null);
        }
    }

    private sealed class FakeHubContext : IHubContext<LiveHub>
    {
        public List<(string Group, string Method, object? Arg)> Messages { get; } = [];
        public IHubClients Clients { get; }
        public IGroupManager Groups { get; } = new FakeGroupManager();

        public FakeHubContext() => this.Clients = new FakeHubClients(this.Messages);

        private sealed class FakeHubClients(List<(string, string, object?)> messages) : IHubClients
        {
            public IClientProxy Group(string groupName) => new FakeClientProxy(groupName, messages);
            public IClientProxy All => throw new NotImplementedException();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
            public IClientProxy Client(string connectionId) => throw new NotImplementedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotImplementedException();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotImplementedException();
            public IClientProxy User(string userId) => throw new NotImplementedException();
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotImplementedException();
        }

        private sealed class FakeClientProxy(string groupName, List<(string, string, object?)> messages) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                messages.Add((groupName, method, args.Length > 0 ? args[0] : null));
                return Task.CompletedTask;
            }
        }

        private sealed class FakeGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }
    }
}
