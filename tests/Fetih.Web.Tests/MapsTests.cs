namespace Fetih.Web.Tests;

using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fetih.Game.States;
using Fetih.Game.World;
using Fetih.Web.Features.Catalogs;
using Fetih.Web.Features.Maps;
using Xunit;

[Collection(WebCollection.Name)]
public class MapsTests(WebFixture web)
{
    [Fact]
    public void EncodeGridPacksWalkabilityAndElevation()
    {
        // 3x3 grid: 9 cells (2 bytes for walkability bitmap)
        var dmap = BuildDMap(3, 3,
            walkable: (x, y) => (x + y) % 2 == 0,
            elevation: (x, y) => (short)(y * 100 + x * 10 - 50),
            portals: [(1, 2, 0)]);
        var grid = MapGrid.Parse(dmap);
        var encoded = MapDataStore.EncodeGrid(1002, grid);

        Assert.Equal(1002u, encoded.MapId);
        Assert.Equal(3, encoded.Width);
        Assert.Equal(3, encoded.Height);
        Assert.Equal(-50, encoded.MinElevation);
        Assert.Equal(170, encoded.MaxElevation);

        var walkBytes = Convert.FromBase64String(encoded.WalkableBase64);
        Assert.Equal(2, walkBytes.Length);
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
        {
            int idx = y * 3 + x;
            bool bit = (walkBytes[idx >> 3] & (1 << (idx & 7))) != 0;
            Assert.Equal((x + y) % 2 == 0, bit);
        }

        var elevBytes = Convert.FromBase64String(encoded.ElevationBase64);
        Assert.Equal(9, elevBytes.Length);
        Assert.Equal(0, elevBytes[0]);       // (0,0) = -50 -> 0
        Assert.Equal(255, elevBytes[8]);     // (2,2) = 170 -> 255
    }

    [Fact]
    public void StoreMergesCatalogDataDMapAndGuildWar()
    {
        var temp = Path.Combine(Path.GetTempPath(), "fetih-maps-test-" + Guid.NewGuid().ToString("N")[..8]);
        var assets = Path.Combine(temp, "assets");
        var data = Path.Combine(temp, "data");
        var client = Path.Combine(temp, "client");
        Directory.CreateDirectory(Path.Combine(assets, "maps", "1002"));
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(Path.Combine(client, "ini"));
        Directory.CreateDirectory(Path.Combine(client, "map"));

        try
        {
            File.WriteAllText(Path.Combine(assets, "maps.json"), """
                [
                  { "id": 1002, "name": "Twin City", "serverName": "twin_city", "aliases": ["tc"], "file": "map/newplain.dmap", "puzzleSize": 256, "minimap": "minimaps/1002.png" },
                  { "id": 1038, "name": "Guild Area", "serverName": "guild_war", "aliases": ["gw"] }
                ]
                """);
            File.WriteAllText(Path.Combine(assets, "monsters.json"), """
                [
                  { "id": 1, "name": "Pheasant", "level": 1, "life": 20 }
                ]
                """);
            File.WriteAllText(Path.Combine(assets, "faces.json"), """
                { "npc": { "1": "faces/npc/1.png" }, "player": {} }
                """);
            File.WriteAllText(Path.Combine(assets, "maps", "1002", "meta.json"), """
                {
                  "width": 1024, "height": 512, "tileSize": 256, "maxZoom": 2,
                  "cell": { "width": 64, "height": 32 },
                  "mapSize": { "width": 4, "height": 4 },
                  "origin": { "x": 128, "y": 16 },
                  "background": { "x": 0, "y": 0 },
                  "puzzle": { "width": 4, "height": 2, "gridSize": 256 },
                  "ground": "maps/tiles/newplain/ground/{z}/{x}/{y}.webp",
                  "covers": "maps/tiles/newplain/covers/{z}/{x}/{y}.webp",
                  "animations": [
                    { "x": 10, "y": 20, "width": 30, "height": 40, "interval": 100, "frames": ["maps/tiles/newplain/anim/0/0.png", "maps/tiles/newplain/anim/0/1.png"] }
                  ]
                }
                """);

            File.WriteAllText(Path.Combine(data, "maps.json"), """
                { "maps": [
                  { "map": 1002, "name": "twin_city", "aliases": ["tc"], "flags": "PkDisable", "portal": { "x": 2, "y": 2 }, "reborn": 1002, "scroll": 1060020, "ridePlus": 4 },
                  { "map": 1038, "name": "guild_war", "flags": "GuildMap", "reborn": 1002 }
                ] }
                """);
            File.WriteAllText(Path.Combine(data, "npcs.json"), """
                { "npcs": [
                  { "id": 10050, "lookface": 280, "role": 29, "map": 1002, "x": 1, "y": 1 },
                  { "id": 6700, "lookface": 1135, "role": 10, "map": 1038, "x": 84, "y": 99, "life": 20000000 },
                  { "id": 6701, "lookface": 241, "role": 26, "map": 1038, "x": 163, "y": 211, "life": 10000000 },
                  { "id": 6702, "lookface": 271, "role": 26, "map": 1038, "x": 223, "y": 177, "life": 10000000 },
                  { "id": 101611, "lookface": 1487, "role": 2, "map": 1038, "x": 348, "y": 320 }
                ] }
                """);
            File.WriteAllText(Path.Combine(data, "spawns.json"), """
                { "spawns": [
                  { "id": 1, "map": 1002, "x": 2, "y": 3, "spread": 5, "amount": 10, "respawn": 15, "monster": 1 }
                ] }
                """);
            File.WriteAllText(Path.Combine(data, "portals.json"), """
                { "portals": [
                  { "map": 1002, "portal": 0, "toMap": 1038, "toX": 350, "toY": 339 }
                ] }
                """);
            File.WriteAllText(Path.Combine(data, "teleporters.json"), """
                { "teleporters": [
                  { "id": 10050, "fare": 100, "face": 1, "text": ["Where to?"], "destinations": [{ "name": "Guild Area", "map": 1038, "x": 350, "y": 339 }] }
                ] }
                """);

            File.WriteAllText(Path.Combine(client, "ini", "npc.ini"), "[NpcType28]\nName=Conductress\n");
            File.WriteAllBytes(Path.Combine(client, "ini", "GameMap.dat"), BuildGameMapDat([(1002, "map/newplain.dmap", 256)]));
            File.WriteAllBytes(Path.Combine(client, "map", "newplain.dmap"),
                BuildDMap(4, 4, (x, y) => x != 0, (_, _) => 0, [(3, 2, 0)]));

            var catalogs = new CatalogStore(assets, data, client);
            var store = new MapDataStore(assets, data, client, catalogs);

            var list = store.ListMaps();
            Assert.Equal(2, list.Length);
            var tc = list.Single(m => m.Id == 1002);
            Assert.True(tc.HasTiles);
            Assert.Equal(1, tc.NpcCount);
            Assert.Equal(1, tc.SpawnCount);
            Assert.Equal(1, tc.PortalCount);

            var detail = store.GetMapDetail(1002);
            Assert.NotNull(detail);
            Assert.True(detail.HasTiles);
            Assert.True(detail.HasGrid);
            var animation = Assert.Single(detail.Meta!.Animations!);
            Assert.Equal(100, animation.Interval);
            Assert.Equal(2, animation.Frames.Length);
            Assert.Equal(4, detail.Width);
            Assert.Equal(4, detail.Height);
            Assert.Single(detail.Npcs);
            Assert.Equal("Conductress", detail.Npcs[0].Name);
            Assert.Equal("teleporter", detail.Npcs[0].Kind);
            Assert.Equal("faces/npc/1.png", detail.Npcs[0].FacePath);
            Assert.Single(detail.Spawns);
            Assert.Equal("Pheasant", detail.Spawns[0].MonsterName);
            Assert.Single(detail.Portals);
            Assert.Equal((ushort)3, detail.Portals[0].X);
            Assert.Equal(1038u, detail.Portals[0].ToMap);
            Assert.Single(detail.Teleporters);
            Assert.Equal(100u, detail.Teleporters[0].Fare);

            var grid = store.GetMapGrid(1002);
            Assert.NotNull(grid);
            Assert.Equal(4, grid.Width);

            var gw = store.GetMapDetail(1038);
            Assert.NotNull(gw?.GuildWar);
            Assert.NotNull(gw.GuildWar.Pole);
            Assert.NotNull(gw.GuildWar.LeftGate);
            Assert.NotNull(gw.GuildWar.RightGate);
            Assert.Single(gw.GuildWar.Conductors);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task EndpointsEnforcePermissionAndReturnMaps()
    {
        var anon = web.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/maps")).StatusCode);

        var player = await web.LoginAsync(await web.CreateAccountAsync((ushort)Authority.Player), "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/maps")).StatusCode);

        var mod = await web.LoginAsync(await web.CreateAccountAsync((ushort)Authority.Moderator), "secret1");
        var listResp = await mod.GetAsync("/api/maps?search=twin");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<MapListPage>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(list);
        Assert.Contains(list.Items, m => m.Id == 1002);

        var detailResp = await mod.GetAsync("/api/maps/1002");
        detailResp.EnsureSuccessStatusCode();
        var detail = await detailResp.Content.ReadFromJsonAsync<MapDetail>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(detail);
        Assert.Equal(1002u, detail.Id);
        Assert.NotEmpty(detail.Npcs);
        Assert.NotEmpty(detail.Portals);

        Assert.Equal(HttpStatusCode.NotFound, (await mod.GetAsync("/api/maps/999999")).StatusCode);
    }

    internal static byte[] BuildDMap(int width, int height, Func<int, int, bool> walkable, Func<int, int, short> elevation,
        (uint X, uint Y, uint Index)[] portals)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.Latin1);
        bw.Write(1004u); // version
        bw.Write(0u);    // data
        bw.Write(new byte[260]); // puzzle path
        bw.Write((uint)width);
        bw.Write((uint)height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bw.Write((ushort)(walkable(x, y) ? 0 : 1));
                bw.Write((ushort)0);
                bw.Write(elevation(x, y));
            }
            bw.Write(0u); // row checksum
        }
        bw.Write((uint)portals.Length);
        foreach (var (x, y, idx) in portals)
        {
            bw.Write(x);
            bw.Write(y);
            bw.Write(idx);
        }
        bw.Write(0u); // object count
        return ms.ToArray();
    }

    private static byte[] BuildGameMapDat((uint Id, string Path, uint PuzzleSize)[] entries)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.Latin1);
        bw.Write((uint)entries.Length);
        foreach (var (id, path, puzzleSize) in entries)
        {
            var bytes = Encoding.Latin1.GetBytes(path);
            bw.Write(id);
            bw.Write((uint)bytes.Length);
            bw.Write(bytes);
            bw.Write(puzzleSize);
        }
        return ms.ToArray();
    }
}
