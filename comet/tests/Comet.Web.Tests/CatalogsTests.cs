namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using Comet.Web.Features.Catalogs;
using Xunit;

[Collection(WebCollection.Name)]
public class CatalogsTests(WebFixture web)
{
    [Fact]
    public void ShopParser_ParsesAllFormats()
    {
        var shopDat = ShopParser.ParseShopDat("""
            [Header]
            Amount=2

            [Shop0]
            ID=3
            Name=Drugstore
            Type=1
            MoneyType=0
            ItemAmount=2
            Item0=1000000
            Item1=1000010

            [Shop1]
            ID=3
            Name=DuplicateIgnored
            MoneyType=1
            ItemAmount=1
            Item0=999
            """);
        Assert.Single(shopDat);
        Assert.Equal((uint)3, shopDat[0].NpcId);
        Assert.Equal("silver", shopDat[0].Currency);
        Assert.Equal(2, shopDat[0].Items.Count);

        var emoney = ShopParser.ParseEmoneyShop("""
            ID=432
            MoneyType=1

            [item]
            Amount=2
            Item0=203009 new
            Item1=724002 null

            [recommend]
            Item0=203009 null
            """);
        Assert.Single(emoney);
        Assert.Equal((uint)432, emoney[0].NpcId);
        Assert.Equal(2, emoney[0].Items.Count);
        Assert.Equal(["new", "recommend"], emoney[0].Items[0].Tags);
        Assert.Empty(emoney[0].Items[1].Tags);

        var emoneyV2 = ShopParser.ParseEmoneyShopV2("""
            ID=2888
            MoneyType=1

            511028 equip|new A~powerful~scythe.
            724002 item|hot Open~it!
            [recommend]
            511028 equip|new A~powerful~scythe.
            """);
        Assert.Single(emoneyV2);
        Assert.Equal(2, emoneyV2[0].Items.Count);
        Assert.Equal("equip", emoneyV2[0].Items[0].Category);
        Assert.Equal(["new", "recommend"], emoneyV2[0].Items[0].Tags);
        Assert.Equal("A powerful scythe.", emoneyV2[0].Items[0].Description);

        var outerwear = ShopParser.ParseOuterwearShop("""
            [item]
            Amount=1
            Item0=183325_new
            [overcoat]
            Item0=181355_null
            """);
        Assert.Single(outerwear);
        Assert.Equal(2, outerwear[0].Items.Count);
        Assert.Equal(["new"], outerwear[0].Items[0].Tags);
        Assert.Equal("overcoat", outerwear[0].Items[1].Category);

        var honor = ShopParser.ParseHonorShop("""
            ItemAmount=2
            200006,300000,0,item,,new
            200002,800000,0,item,recommend,new
            """);
        Assert.Single(honor);
        Assert.Equal("honor", honor[0].Currency);
        Assert.Equal((uint)300000, honor[0].Items[0].Price);
        Assert.Equal(["recommend", "new"], honor[0].Items[1].Tags);

        var race = ShopParser.ParseRacePointShop("""
            [Normal]
            200000 25000 item other new
            [recommend]
            200000 25000 item other hot
            """);
        Assert.Single(race);
        Assert.Equal("race_points", race[0].Currency);
        Assert.Single(race[0].Items);
        Assert.Equal(["new", "recommend", "hot"], race[0].Items[0].Tags);
    }

    [Fact]
    public void CatalogStore_HandlesMissingDirectoriesGracefully()
    {
        var store = new CatalogStore("/nonexistent/assets", "/nonexistent/data", "/nonexistent/client");
        Assert.Empty(store.Monsters);
        Assert.Empty(store.Spells);
        Assert.Empty(store.Maps);
        Assert.Empty(store.Faces.Npc);
        Assert.Empty(store.Faces.Player);
        Assert.Empty(store.Shops);
    }

    [Fact]
    public void CatalogStore_LoadsAndEnrichesFromDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "comet-catalogs-test-" + Guid.NewGuid().ToString("N")[..8]);
        var assets = Path.Combine(root, "assets");
        var data = Path.Combine(root, "data");
        var ini = Path.Combine(root, "client", "ini");
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(ini);

        try
        {
            File.WriteAllText(Path.Combine(assets, "monsters.json"), """
                [
                  { "id": 1, "name": "Pheasant", "level": 1, "life": 33, "boss": false, "client": { "Level": 1, "MaxLife": 33, "ZoomPercent": 100 } }
                ]
                """);
            File.WriteAllText(Path.Combine(assets, "magic.json"), """
                [
                  { "type": 1000, "name": "Thunder", "sort": "Attack", "icon": "icons/magic/1000.png", "levels": [{ "level": 0, "power": 30110, "useMana": 15 }] }
                ]
                """);
            File.WriteAllText(Path.Combine(assets, "maps.json"), """
                [
                  { "id": 1002, "name": "Twin City", "serverName": "twin_city", "aliases": ["tc"], "minimap": "minimaps/1002.png" }
                ]
                """);
            File.WriteAllText(Path.Combine(data, "maps.json"), """
                { "maps": [ { "map": 1002, "flags": "PkDisable", "reborn": 1002 } ] }
                """);
            File.WriteAllText(Path.Combine(assets, "faces.json"), """
                { "npc": { "10": "faces/npc/10.png" }, "player": { "1": "faces/player/1.png" } }
                """);
            File.WriteAllText(Path.Combine(assets, "items.json"), """
                [
                  { "id": 1000000, "name": "Stancher", "icon": "icons/items/1000000.png", "price": 100 }
                ]
                """);
            File.WriteAllText(Path.Combine(data, "npcs.json"), """
                { "npcs": [ { "id": 3, "map": 1002, "x": 400, "y": 350 } ] }
                """);
            File.WriteAllText(Path.Combine(ini, "Shop.dat"), """
                [Shop0]
                ID=3
                Name=Drugstore
                MoneyType=0
                ItemAmount=1
                Item0=1000000
                """);

            var store = new CatalogStore(assets, data, Path.Combine(root, "client"));
            Assert.Single(store.Monsters);
            Assert.Equal(100, store.Monsters[0].ZoomPercent);
            Assert.Single(store.Spells);
            Assert.Equal("Thunder", store.Spells[0].Name);
            Assert.Single(store.Maps);
            Assert.Equal("PkDisable", store.Maps[0].Flags);
            Assert.Single(store.Faces.Npc);
            Assert.Single(store.Faces.Player);
            Assert.Single(store.Shops);
            Assert.Equal((uint)1002, store.Shops[0].MapId);
            Assert.Equal("Stancher", store.Shops[0].Items[0].ItemName);
            Assert.Equal((uint)100, store.Shops[0].Items[0].Price);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CatalogEndpoints_EnforcePermissionsAndReturnOk()
    {
        using var anon = web.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/catalogs/monsters")).StatusCode);

        var playerEmail = await web.CreateAccountAsync(authority: 1);
        using var player = await web.LoginAsync(playerEmail, "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/catalogs/monsters")).StatusCode);

        var modEmail = await web.CreateAccountAsync(authority: 2);
        using var mod = await web.LoginAsync(modEmail, "secret1");

        var monsters = await mod.GetFromJsonAsync<MonsterCatalogPage>("/api/catalogs/monsters");
        Assert.NotNull(monsters);
        var spells = await mod.GetFromJsonAsync<SpellCatalogPage>("/api/catalogs/spells");
        Assert.NotNull(spells);
        var maps = await mod.GetFromJsonAsync<MapCatalogPage>("/api/catalogs/maps");
        Assert.NotNull(maps);
        var faces = await mod.GetFromJsonAsync<FacesCatalogResponse>("/api/catalogs/faces");
        Assert.NotNull(faces);
        var shops = await mod.GetFromJsonAsync<ShopCatalogResponse>("/api/catalogs/shops");
        Assert.NotNull(shops);

        Assert.Equal(HttpStatusCode.NotFound, (await mod.GetAsync("/api/catalogs/monsters/9999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mod.GetAsync("/api/catalogs/spells/65000")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mod.GetAsync("/api/catalogs/maps/9999999")).StatusCode);
    }
}
