namespace Fetih.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fetih.Web.Audit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Collection(WebCollection.Name)]
public class AuthTests(WebFixture fixture)
{
    [Fact]
    public async Task SeedAccountLogsInAsPlayer()
    {
        var client = await fixture.LoginAsync("test", "test");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("test", me.GetProperty("username").GetString());
        Assert.Equal(1, me.GetProperty("authority").GetInt32());
        Assert.Empty(me.GetProperty("permissions").EnumerateArray());
    }

    [Fact]
    public async Task PlayerIsRefusedTheAdminArea()
    {
        var client = await fixture.LoginAsync("test", "test");
        var response = await client.GetAsync("/api/audit");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WrongPasswordIsRefused()
    {
        var client = fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "test", password = "wrong1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_credentials", body.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task UnknownAccountIsRefusedLikeAWrongPassword()
    {
        var response = await fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "nobody@test.local", password = "secret1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UsernameIsCaseInsensitive()
    {
        string email = await fixture.CreateAccountAsync(authority: 1);
        await fixture.LoginAsync(email.ToUpperInvariant(), "secret1");
    }

    [Fact]
    public async Task BannedAccountIsRefused()
    {
        string email = await fixture.CreateAccountAsync(authority: 5, status: 5);
        var response = await fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("banned", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task LogoutEndsTheSession()
    {
        var client = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task ModeratorGetsTheAdminAreaButNotTheStudio()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.LoginAsync(email, "secret1");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        var permissions = me.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("AdminArea", permissions);
        Assert.DoesNotContain("Studio", permissions);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit")).StatusCode);
    }

    [Fact]
    public async Task AuditedActionIsListed()
    {
        string email = await fixture.CreateAccountAsync(authority: 5);
        var client = await fixture.LoginAsync(email, "secret1");

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
            await audit.WriteAsync("test.action", "character", "1000001",
                before: new { level = 1 }, after: new { level = 2 }, summary: "level 1 to 2");
        }

        var page = await client.GetFromJsonAsync<JsonElement>("/api/audit?action=test.&targetId=1000001");
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        var entry = page.GetProperty("items")[0];
        Assert.Equal("test.action", entry.GetProperty("action").GetString());
        Assert.Equal("{\"level\":2}", entry.GetProperty("after").GetString());
    }

    [Fact]
    public async Task UnknownApiPathIsNotFound()
    {
        var response = await fixture.Factory.CreateClient().GetAsync("/api/nothing-here");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HealthSeesTheDatabasesAndAnOfflineGame()
    {
        var health = await fixture.Factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/health");
        Assert.True(health.GetProperty("gameDatabase").GetBoolean());
        Assert.True(health.GetProperty("accountDatabase").GetBoolean());
        Assert.False(health.GetProperty("gameServer").GetBoolean());
    }
}
