namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comet.Web.Features.Dashboard;
using Comet.Web.Features.Economy;
using Comet.Web.Features.Logs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Collection(WebCollection.Name)]
public class EnvironmentTests(WebFixture fixture)
{
    [Fact]
    public async Task StaffOnlyRefusesPlayersAndPublicRegistration()
    {
        var config = fixture.Factory.Services.GetRequiredService<WebConfiguration>();
        bool prevStaffOnly = config.Web.StaffOnly;
        config.Web.StaffOnly = true;
        try
        {
            var client = fixture.Factory.CreateClient();

            var playerLogin = await client.PostAsJsonAsync("/api/auth/login",
                new { username = "test", password = "test" });
            Assert.Equal(HttpStatusCode.Forbidden, playerLogin.StatusCode);
            var playerBody = await playerLogin.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("staff_only", playerBody.GetProperty("error").GetString());

            var register = await client.PostAsJsonAsync("/api/public/account/register",
                new { username = "newplayer@test.local", password = "secret1" });
            Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);
            var regBody = await register.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("staff_only", regBody.GetProperty("error").GetString());

            string modEmail = await fixture.CreateAccountAsync(authority: 2);
            var modLogin = await client.PostAsJsonAsync("/api/auth/login",
                new { username = modEmail, password = "secret1" });
            Assert.Equal(HttpStatusCode.OK, modLogin.StatusCode);
        }
        finally
        {
            config.Web.StaffOnly = prevStaffOnly;
        }
    }

    [Fact]
    public async Task StudioReadOnlyAllowsReadsAndBlocksWrites()
    {
        var config = fixture.Factory.Services.GetRequiredService<WebConfiguration>();
        bool prevReadOnly = config.Web.StudioReadOnly;
        config.Web.StudioReadOnly = true;
        try
        {
            string adminEmail = await fixture.CreateAccountAsync(authority: 5);
            var client = await fixture.LoginAsync(adminEmail, "secret1");

            var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
            Assert.True(me.GetProperty("studioReadOnly").GetBoolean());

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/data/levels")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/changes")).StatusCode);

            var reload = await client.PostAsync("/api/studio/monsters/reload", null);
            Assert.Equal(HttpStatusCode.Forbidden, reload.StatusCode);
            var reloadBody = await reload.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("studio_read_only", reloadBody.GetProperty("error").GetString());

            var discard = await client.PostAsync("/api/changes/level.json/discard", null);
            Assert.Equal(HttpStatusCode.Forbidden, discard.StatusCode);
        }
        finally
        {
            config.Web.StudioReadOnly = prevReadOnly;
        }
    }

    [Fact]
    public void ObservabilityEnvironmentScopesQueries()
    {
        Assert.Equal("{env=~\"test|shared\", server=\"game\", event=\"npc.talk\"} | json npcId",
            LogQl.Scope("{server=\"game\", event=\"npc.talk\"} | json npcId", "test"));
        Assert.Equal(
            "sum(sum_over_time({env=~\"prod|shared\", server=\"game\", event=\"item.picked_up\"} | json silver | unwrap silver [60s]))",
            LogQl.Scope(EconomyFlowAggregator.Query(EconomyFlowAggregator.PickupQuery, TimeSpan.FromMinutes(1)), "prod"));
        Assert.Equal("{level=~\"error|fatal\"}", LogQl.Scope(LogStats.ErrorSelector, null));

        var test = new ObservabilityOptions { Environment = "test" };
        Assert.Equal("comet_economy_silver_total{env=\"test\"}", test.ScopeMetric("comet_economy_silver_total"));
        Assert.EndsWith("&var-env=test", LogQl.BuildGrafanaUrl("http://localhost:3000", new LogQueryFilter(),
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, "test"));
    }
}
