namespace Fetih.Web.Tests;

using System.Net;
using Fetih.Web.Features.Catalogs;
using Fetih.Web.Features.Items;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

[Collection(WebCollection.Name)]
public class ReferencePagesTests(WebFixture fixture)
{
    [Theory]
    [InlineData("/admin/items")]
    [InlineData("/admin/items?q=1&category=Weapon&minLevel=1&sayfa=2")]
    [InlineData("/admin/catalogs/spells")]
    [InlineData("/admin/catalogs/shops")]
    public async Task ModeratorSeesTheTable(string url)
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email, "secret1");
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/admin/items")]
    [InlineData("/admin/items/410009")]
    [InlineData("/admin/catalogs/spells")]
    [InlineData("/admin/catalogs/shops")]
    public async Task PlayerIsForbidden(string url)
    {
        var client = await fixture.SignInAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("/admin/items/4294967295")]
    [InlineData("/admin/items/-1")]
    public async Task UnknownItemIsNotFound(string url)
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email, "secret1");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task PopulatedTablesRenderTooltipsDetailSpellsAndShops()
    {
        string root = Path.Combine(Path.GetTempPath(), "fetih-ref-test-" + Guid.NewGuid().ToString("N")[..8]);
        string assets = Path.Combine(root, "assets");
        string data = Path.Combine(root, "data");
        string ini = Path.Combine(root, "client", "ini");
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(ini);

        try
        {
            File.WriteAllText(Path.Combine(assets, "items.json"), """
                [
                  {
                    "id": 410009, "name": "SuperBlade", "type": "Blade", "description": "Keskin kilic",
                    "icon": "icons/items/410009.png", "groundIcon": "icons/ground/410009.png",
                    "requiredProfession": 11, "requiredLevel": 15, "requiredGender": 1,
                    "minAttack": 50, "maxAttack": 80, "amount": 1000, "amountLimit": 1200,
                    "gem1": 13, "gem2": 255, "monopoly": 3, "price": 5000, "emoneyPrice": 10
                  },
                  { "id": 1000000, "name": "Stancher", "icon": "icons/items/1000000.png", "price": 100 },
                  { "id": 1000010, "name": "Amrita", "icon": "icons/items/1000010.png" }
                ]
                """);
            File.WriteAllText(Path.Combine(assets, "magic.json"), """
                [
                  { "type": 1000, "name": "Thunder", "sort": "Attack", "useXp": 0, "levels": [{ "level": 0, "power": 30110, "useMana": 15, "needLevel": 10 }] },
                  { "type": 1010, "name": "Cyclone", "sort": "AttachStatus", "useXp": 1, "levels": [{ "level": 0, "power": 100, "useMana": 0, "needLevel": 15 }] }
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
                ItemAmount=2
                Item0=1000000
                Item1=1000010
                """);

            var itemsCatalog = new ItemCatalog(Path.Combine(assets, "items.json"));
            var catalogStore = new CatalogStore(assets, data, Path.Combine(root, "client"));

            using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ItemCatalog>();
                    services.RemoveAll<CatalogStore>();
                    services.AddSingleton(itemsCatalog);
                    services.AddSingleton(catalogStore);
                });
            });

            string email = await fixture.CreateAccountAsync(authority: 2);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var login = await WebFixture.PostFormAsync(client, "/giris", new() { ["kullanici"] = email, ["sifre"] = "secret1" });
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

            // Items list: quality-colored tooltip, stats, clamping out-of-range page
            string itemsHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/items?sayfa=99"));
            Assert.Contains("SuperBlade", itemsHtml);
            Assert.Contains("color:#ebe57f", itemsHtml);
            Assert.Contains("Süper Ejderha Cevheri", itemsHtml);
            Assert.Contains("Dayanıklılık: 10/12", itemsHtml);
            Assert.Contains("Keskin kilic", itemsHtml);

            // Item detail: Turkish labels, GroundIcon, Grafana link
            string detailHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/items/410009"));
            Assert.Contains("Süper", detailHtml);
            Assert.Contains("Trojan", detailHtml);
            Assert.Contains("Erkek", detailHtml);
            Assert.Contains("Boş Yuva", detailHtml);
            Assert.Contains("icons/ground/410009.png", detailHtml);
            Assert.Contains("/grafana/d/fetih-items/items?var-itemId=410009", detailHtml);

            // Spells: percent power formatting and XP-only filter
            string spellsHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/catalogs/spells"));
            Assert.Contains("Thunder", spellsHtml);
            Assert.Contains("110%", spellsHtml);
            string xpSpellsHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/catalogs/spells?xpOnly=true"));
            Assert.Contains("Cyclone", xpSpellsHtml);
            Assert.DoesNotContain("Thunder", xpSpellsHtml);

            // Shops: Turkish currency, null price fallback, location, item search narrowing & auto-open
            string shopsHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/catalogs/shops"));
            Assert.Contains("Drugstore", shopsHtml);
            Assert.Contains("Gümüş", shopsHtml);
            Assert.Contains("Harita #1002 (400, 350)", shopsHtml);
            Assert.Contains("<td>—</td>", shopsHtml);

            string shopSearchHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/catalogs/shops?q=Stancher"));
            Assert.Contains("<details open", shopSearchHtml);
            Assert.Contains("Stancher", shopSearchHtml);
            Assert.DoesNotContain("Amrita", shopSearchHtml);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
