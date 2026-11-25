namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

[Collection(WebCollection.Name)]
public class ServerTests(WebFixture fixture)
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task NonAdministratorsAreForbiddenFromServerControl(ushort authority)
    {
        string email = await fixture.CreateAccountAsync(authority);
        var client = await fixture.LoginAsync(email, "secret1");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/server/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/server/maintenance",
            new { enabled = true, message = "Maint", kickNonGm = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/server/motd",
            new { text = "Hello" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/server/shutdown",
            new { mode = "Restart", seconds = 60, reason = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/server/shutdown/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task AdministratorCanUpdateMaintenanceAndMotdWithAudit()
    {
        string email = await fixture.CreateAccountAsync(authority: 5);
        var client = await fixture.LoginAsync(email, "secret1");

        var initial = await client.GetFromJsonAsync<JsonElement>("/api/server/status");
        Assert.False(initial.GetProperty("gameConnected").GetBoolean());

        var maintResp = await client.PutAsJsonAsync("/api/server/maintenance", new
        {
            enabled = true,
            message = "Upgrading server hardware",
            kickNonGm = false,
        });
        Assert.Equal(HttpStatusCode.OK, maintResp.StatusCode);
        var maintBody = await maintResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(maintBody.GetProperty("ok").GetBoolean());
        Assert.False(maintBody.GetProperty("appliedLive").GetBoolean());

        var motdResp = await client.PutAsJsonAsync("/api/server/motd", new
        {
            text = "Welcome to Fetih!\nDouble EXP weekend is active.",
        });
        Assert.Equal(HttpStatusCode.OK, motdResp.StatusCode);

        var status = await client.GetFromJsonAsync<JsonElement>("/api/server/status");
        Assert.True(status.GetProperty("maintenanceEnabled").GetBoolean());
        Assert.Equal("Upgrading server hardware", status.GetProperty("maintenanceMessage").GetString());
        Assert.Equal("Welcome to Fetih!\nDouble EXP weekend is active.", status.GetProperty("motd").GetString());

        // Reset back to disabled
        await client.PutAsJsonAsync("/api/server/maintenance", new
        {
            enabled = false,
            message = "",
            kickNonGm = false,
        });

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/audit?action=server.");
        var actions = audit.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("action").GetString())
            .ToList();
        Assert.Contains("server.maintenance", actions);
        Assert.Contains("server.motd", actions);
    }

    [Fact]
    public async Task ShutdownEndpointsValidateAndReportOfflineGame()
    {
        string email = await fixture.CreateAccountAsync(authority: 5);
        var client = await fixture.LoginAsync(email, "secret1");

        var badCountdown = await client.PostAsJsonAsync("/api/server/shutdown", new
        {
            mode = "Restart",
            seconds = 0,
            reason = "",
        });
        Assert.Equal(HttpStatusCode.BadRequest, badCountdown.StatusCode);
        Assert.Equal("invalid_countdown",
            (await badCountdown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var offlineStart = await client.PostAsJsonAsync("/api/server/shutdown", new
        {
            mode = "Restart",
            seconds = 60,
            reason = "Patch",
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, offlineStart.StatusCode);
        Assert.Equal("game_offline",
            (await offlineStart.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var offlineCancel = await client.PostAsync("/api/server/shutdown/cancel", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, offlineCancel.StatusCode);
        Assert.Equal("game_offline",
            (await offlineCancel.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }
}
