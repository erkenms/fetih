namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comet.Web.Features.Items;
using Xunit;

public class ItemsCatalogTests : IDisposable
{
    private readonly string tempDir;

    public ItemsCatalogTests()
    {
        this.tempDir = Path.Combine(Path.GetTempPath(), "comet-items-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(this.tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.tempDir))
            Directory.Delete(this.tempDir, recursive: true);
    }

    private string WriteSampleItems()
    {
        string path = Path.Combine(this.tempDir, "items.json");
        var sample = new object[]
        {
            new
            {
                id = 1088000,
                name = "DragonBall",
                type = "Quest/Special",
                description = "Increases equipment quality.",
                icon = "icons/items/Item1088000.png",
                price = 1000,
                emoneyPrice = 215,
                actionId = 1088000,
            },
            new
            {
                id = 410009,
                name = "PhoenixBlade",
                type = "Blade",
                description = "A sharp super blade.",
                icon = "icons/items/Item410009.png",
                requiredProfession = 10,
                requiredLevel = 15,
                requiredStrength = 25,
                minAttack = 45,
                maxAttack = 72,
                amount = 4000,
                amountLimit = 4000,
                attackRange = 2,
                attackSpeed = 500,
                price = 3200,
            },
            new
            {
                id = 130008,
                name = "DragonArmor",
                type = "Armor",
                requiredProfession = 20,
                requiredLevel = 70,
                defense = 180,
                magicDefense = 30,
                amount = 6000,
                amountLimit = 6000,
            },
            new
            {
                id = 700013,
                name = "SuperDragonGem",
                type = "Gem",
                description = "Increases physical attack by 15%.",
                price = 5000,
            },
            new
            {
                id = 1000000,
                name = "Stancher",
                type = "Medicine",
                life = 200,
                price = 50,
                stackLimit = 10,
            },
        };
        File.WriteAllText(path, JsonSerializer.Serialize(sample));
        return path;
    }

    [Fact]
    public void MissingAssetsReturnsEmptyWithoutThrowing()
    {
        var catalog = new ItemCatalog(Path.Combine(this.tempDir, "missing.json"));
        var (items, total, available) = catalog.Search("dragon", null, null, null, null, null, null, 1, 50);
        Assert.False(available);
        Assert.Empty(items);
        Assert.Equal(0, total);
        Assert.False(catalog.TryGet(1088000, out _));
    }

    [Fact]
    public void SearchFindsDragonBallAndOrdersByRelevance()
    {
        var catalog = new ItemCatalog(this.WriteSampleItems());
        var (items, total, available) = catalog.Search("dragon", null, null, null, null, null, null, 1, 50);

        Assert.True(available);
        Assert.Equal(3, total);
        Assert.Contains(items.Take(2), x => x.Id == 1088000 && x.Name == "DragonBall" && x.Icon == "icons/items/Item1088000.png");
        Assert.Equal((uint)700013, items[2].Id);
    }

    [Fact]
    public void ExactIdAndFiltersWork()
    {
        var catalog = new ItemCatalog(this.WriteSampleItems());

        var (byId, _, _) = catalog.Search("410009", null, null, null, null, null, null, 1, 50);
        Assert.Single(byId);
        Assert.Equal(ItemCategory.Weapon, byId[0].Category);
        Assert.Equal(ItemQuality.Super, byId[0].Quality);

        var (byCategory, _, _) = catalog.Search(null, ItemCategory.Gem, null, null, null, null, null, 1, 50);
        Assert.Single(byCategory);
        Assert.Equal((uint)700013, byCategory[0].Id);

        var (byProfession, _, _) = catalog.Search(null, null, ItemQuality.Elite, 21, 50, 100, null, 1, 50);
        Assert.Single(byProfession);
        Assert.Equal((uint)130008, byProfession[0].Id);

        var batch = catalog.GetMany([1088000, 9999999, 700013]);
        Assert.Equal(2, batch.Length);
    }
}

[Collection(WebCollection.Name)]
public class ItemsEndpointTests(WebFixture fixture)
{
    [Fact]
    public async Task PlayerIsRefusedItemCatalog()
    {
        var client = await fixture.LoginAsync("test", "test");
        var response = await client.GetAsync("/api/items");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ModeratorCanQueryItemsAndMissingItemReturnsNotFound()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.LoginAsync(email, "secret1");

        var listResponse = await client.GetAsync("/api/items?q=dragon");
        listResponse.EnsureSuccessStatusCode();
        var page = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(page.GetProperty("assetsAvailable").GetBoolean());
        Assert.Equal(0, page.GetProperty("total").GetInt32());

        var notFound = await client.GetAsync("/api/items/9999999");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }
}
