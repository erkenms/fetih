namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MySqlConnector;
using Xunit;

[Collection(WebCollection.Name)]
public class AccountsTests(WebFixture fixture)
{
    private static readonly string DbHost = Environment.GetEnvironmentVariable("COMET_TEST_DB_HOST") is { Length: > 0 } h ? h : "localhost";
    private static readonly string DbUser = Environment.GetEnvironmentVariable("COMET_TEST_DB_USER") is { Length: > 0 } u ? u : "root";
    private static readonly string DbPassword = Environment.GetEnvironmentVariable("COMET_TEST_DB_PASSWORD") is { Length: > 0 } p ? p : "password";

    private string AccountCs() => new MySqlConnectionStringBuilder
    {
        Server = DbHost, UserID = DbUser, Password = DbPassword, Database = fixture.AccountSchema,
    }.ConnectionString;

    [Fact]
    public async Task PermissionsAreEnforcedOnAccounts()
    {
        var player = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/players")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/accounts/1")).StatusCode);

        string modEmail = await fixture.CreateAccountAsync(authority: 2);
        var mod = await fixture.LoginAsync(modEmail, "secret1");
        Assert.Equal(HttpStatusCode.OK, (await mod.GetAsync("/api/players")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.PostAsJsonAsync("/api/accounts/1/ban", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.PostAsJsonAsync("/api/accounts/1/status", new { statusId = 4 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.PostAsJsonAsync("/api/accounts/1/authority", new { authorityId = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.PostAsJsonAsync("/api/accounts/1/reset-password", new { newPassword = "newpassword1" })).StatusCode);

        string gmEmail = await fixture.CreateAccountAsync(authority: 3);
        var gm = await fixture.LoginAsync(gmEmail, "secret1");
        uint playerId = await this.AccountIdAsync(await fixture.CreateAccountAsync(authority: 1));
        // GM can ban (reaches GameRpc -> 503 offline), but cannot change authority or reset password
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await gm.PostAsJsonAsync($"/api/accounts/{playerId}/ban", new { reason = "bot" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gm.PostAsJsonAsync("/api/accounts/1/authority", new { authorityId = 2 })).StatusCode);
    }

    [Fact]
    public async Task StaffCanNotActOnTheirRankOrAbove()
    {
        var gm = await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 3), "secret1");
        uint otherGm = await this.AccountIdAsync(await fixture.CreateAccountAsync(authority: 3));
        uint admin = await this.AccountIdAsync(await fixture.CreateAccountAsync(authority: 5));

        foreach (var target in new[] { otherGm, admin })
            foreach (var action in new[] { "ban", "unban" })
            {
                var response = await gm.PostAsJsonAsync($"/api/accounts/{target}/{action}", new { reason = "x" });
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("outranked", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
            }
        Assert.Equal(HttpStatusCode.NotFound, (await gm.PostAsJsonAsync("/api/accounts/987654321/ban", new { reason = "x" })).StatusCode);
    }

    private async Task<uint> AccountIdAsync(string username)
    {
        await using var db = new MySqlConnection(this.AccountCs());
        await db.OpenAsync();
        var cmd = new MySqlCommand("SELECT AccountID FROM account WHERE Username = @u", db);
        cmd.Parameters.AddWithValue("@u", username);
        return Convert.ToUInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task SearchAndSameIpDiscoveryWork()
    {
        string targetEmail = await fixture.CreateAccountAsync(authority: 1);
        string altEmail = await fixture.CreateAccountAsync(authority: 1);
        uint targetId, altId;

        await using (var db = new MySqlConnection(this.AccountCs()))
        {
            await db.OpenAsync();
            var cmd = new MySqlCommand("SELECT AccountID FROM account WHERE Username = @u", db);
            cmd.Parameters.AddWithValue("@u", targetEmail);
            targetId = Convert.ToUInt32(await cmd.ExecuteScalarAsync());

            cmd.Parameters["@u"].Value = altEmail;
            altId = Convert.ToUInt32(await cmd.ExecuteScalarAsync());

            await new MySqlCommand(
                $"INSERT INTO logins (Timestamp, AccountID, IPAddress) VALUES " +
                $"(UTC_TIMESTAMP(), {targetId}, '203.0.113.42'), " +
                $"(UTC_TIMESTAMP(), {altId}, '203.0.113.42')", db).ExecuteNonQueryAsync();
        }

        string modEmail = await fixture.CreateAccountAsync(authority: 2);
        var mod = await fixture.LoginAsync(modEmail, "secret1");

        var byIp = await mod.GetFromJsonAsync<JsonElement>("/api/players?ip=203.0.113.42");
        Assert.Equal(2, byIp.GetProperty("total").GetInt32());

        var detail = await mod.GetFromJsonAsync<JsonElement>($"/api/accounts/{targetId}");
        Assert.Equal(targetEmail, detail.GetProperty("username").GetString());
        Assert.True(detail.GetProperty("logins").GetArrayLength() >= 1);
        var sameIp = detail.GetProperty("sameIpAccounts").EnumerateArray().ToList();
        Assert.Contains(sameIp, x => x.GetProperty("accountId").GetUInt32() == altId);
    }

    [Fact]
    public async Task AdminCanChangeStatusAuthorityAndResetPasswordWithAudit()
    {
        string adminEmail = await fixture.CreateAccountAsync(authority: 5);
        string targetEmail = await fixture.CreateAccountAsync(authority: 1);
        uint targetId;
        await using (var db = new MySqlConnection(this.AccountCs()))
        {
            await db.OpenAsync();
            var cmd = new MySqlCommand("SELECT AccountID FROM account WHERE Username = @u", db);
            cmd.Parameters.AddWithValue("@u", targetEmail);
            targetId = Convert.ToUInt32(await cmd.ExecuteScalarAsync());
        }

        var admin = await fixture.LoginAsync(adminEmail, "secret1");

        // Status=5 must use ban endpoint
        var badBan = await admin.PostAsJsonAsync($"/api/accounts/{targetId}/status", new { statusId = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, badBan.StatusCode);
        Assert.Equal("use_ban_endpoint", (await badBan.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        // Lock then activate
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/accounts/{targetId}/status", new { statusId = 4, reason = "review" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/accounts/{targetId}/status", new { statusId = 2 })).StatusCode);

        // Promote to moderator
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/accounts/{targetId}/authority", new { authorityId = 2 })).StatusCode);

        // Reset password and verify login with new password works
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/accounts/{targetId}/reset-password", new { newPassword = "newsecret9" })).StatusCode);
        var relogin = await fixture.LoginAsync(targetEmail, "newsecret9");
        Assert.Equal(HttpStatusCode.OK, (await relogin.GetAsync("/api/players")).StatusCode);

        // Verify audit entries exist and password is not leaked
        var audit = await admin.GetFromJsonAsync<JsonElement>($"/api/audit?action=account.&targetId={targetId}");
        var entries = audit.GetProperty("items").EnumerateArray().ToList();
        var actions = entries.Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Contains("account.set_status", actions);
        Assert.Contains("account.set_authority", actions);
        Assert.Contains("account.reset_password", actions);
        Assert.DoesNotContain(entries, e => (e.GetProperty("summary").GetString() ?? "").Contains("newsecret9"));
    }
}
