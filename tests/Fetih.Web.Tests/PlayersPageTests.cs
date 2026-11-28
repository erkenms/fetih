namespace Fetih.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Fetih.Game.Pk;
using Fetih.Web.Auth;
using Fetih.Web.Features.Players;
using Fetih.Web.Game;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;

[Collection(WebCollection.Name)]
public class PlayersPageTests(WebFixture fixture)
{
    private static readonly string DbHost = Environment.GetEnvironmentVariable("FETIH_TEST_DB_HOST") is { Length: > 0 } h ? h : "localhost";
    private static readonly string DbUser = Environment.GetEnvironmentVariable("FETIH_TEST_DB_USER") is { Length: > 0 } u ? u : "root";
    private static readonly string DbPassword = Environment.GetEnvironmentVariable("FETIH_TEST_DB_PASSWORD") is { Length: > 0 } p ? p : "password";

    private string Cs(string schema) => new MySqlConnectionStringBuilder
    {
        Server = DbHost, UserID = DbUser, Password = DbPassword, Database = schema,
    }.ConnectionString;

    private async Task<uint> AccountIdAsync(string username)
    {
        await using var db = new MySqlConnection(this.Cs(fixture.AccountSchema));
        await db.OpenAsync();
        var cmd = new MySqlCommand("SELECT AccountID FROM account WHERE Username = @u", db);
        cmd.Parameters.AddWithValue("@u", username);
        return Convert.ToUInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<uint> CreateCharacterAsync(uint charId, string name, byte cls = 15, uint mapId = 1002, byte level = 70)
    {
        uint accountId = await this.AccountIdAsync(await fixture.CreateAccountAsync(authority: 1));
        await using var db = new MySqlConnection(this.Cs(fixture.GameSchema));
        await db.OpenAsync();
        await new MySqlCommand(
            "INSERT INTO `character` (CharacterID, AccountID, Name, Level, CurrentClass, MapID, X, Y, Registered) VALUES " +
            $"({charId}, {accountId}, '{name}', {level}, {cls}, {mapId}, 100, 100, UTC_TIMESTAMP())", db).ExecuteNonQueryAsync();
        return accountId;
    }

    private static async Task<string> TokenFromAsync(HttpClient client, string path = "/admin/players")
    {
        string page = await client.GetStringAsync(path);
        var match = System.Text.RegularExpressions.Regex.Match(page,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success, "No anti-forgery token on " + path);
        return match.Groups[1].Value;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string token,
        params (string Key, string Value)[] fields)
    {
        var list = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();
        list.Add(new("__RequestVerificationToken", token));
        return client.PostAsync(url, new FormUrlEncodedContent(list));
    }

    [Fact]
    public async Task SignedOutAndPlayerAreBlocked()
    {
        var anon = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await anon.GetAsync("/admin/players");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/giris?donus=%2Fadmin%2Fplayers", resp.Headers.Location?.PathAndQuery);

        var player = await fixture.SignInAsync(WebFixture.Player, WebFixture.PlayerPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/admin/players")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/admin/players/1")).StatusCode);
    }

    [Fact]
    public async Task PermissionsAreEnforcedOnPlayerActions()
    {
        uint targetId = await this.AccountIdAsync(await fixture.CreateAccountAsync(authority: 1));

        var mod = await fixture.SignInAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");
        Assert.Equal(HttpStatusCode.OK, (await mod.GetAsync("/admin/players")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mod.GetAsync($"/admin/players/{targetId}")).StatusCode);

        string modToken = await TokenFromAsync(mod, $"/admin/players/{targetId}");
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(mod, $"/admin/players/{targetId}?handler=Ban", modToken, ("reason", "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(mod, $"/admin/players/{targetId}?handler=Status", modToken, ("statusId", "4"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(mod, $"/admin/players/{targetId}?handler=Authority", modToken, ("authorityId", "2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(mod, $"/admin/players/{targetId}?handler=ResetPassword", modToken, ("newPassword", "newpassword1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(mod, "/admin/players?handler=BulkBan", modToken, ("accountIds", targetId.ToString()))).StatusCode);

        // GM can ban (reaches GameRpc -> offline flash redirect), cannot change authority or reset password
        var gm = await fixture.SignInAsync(await fixture.CreateAccountAsync(authority: 3), "secret1");
        string gmToken = await TokenFromAsync(gm, $"/admin/players/{targetId}");
        var banResp = await PostAsync(gm, $"/admin/players/{targetId}?handler=Ban", gmToken, ("reason", "bot"));
        Assert.Equal(HttpStatusCode.Redirect, banResp.StatusCode);
        Assert.Contains("Oyun sunucusu kapalı; işlem yapılamadı.", await gm.GetStringAsync($"/admin/players/{targetId}"));

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(gm, $"/admin/players/{targetId}?handler=Authority", gmToken, ("authorityId", "2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(gm, $"/admin/players/{targetId}?handler=ResetPassword", gmToken, ("newPassword", "newpassword1"))).StatusCode);
    }

    [Fact]
    public async Task StaffCannotActOnTheirRankOrAboveAndGameOfflineThrowsInService()
    {
        string gmEmail = await fixture.CreateAccountAsync(authority: 3);
        uint gmId = await this.AccountIdAsync(gmEmail);
        var gm = await fixture.SignInAsync(gmEmail, "secret1");
        uint otherGm = await this.AccountIdAsync(await fixture.CreateAccountAsync(authority: 3));
        uint admin = await this.AccountIdAsync(await fixture.CreateAccountAsync(authority: 5));
        uint playerId = await this.AccountIdAsync(await fixture.CreateAccountAsync(authority: 1));

        string token = await TokenFromAsync(gm, $"/admin/players/{otherGm}");
        foreach (uint target in new[] { otherGm, admin })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(gm, $"/admin/players/{target}?handler=Ban", token, ("reason", "x"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(gm, $"/admin/players/{target}?handler=Unban", token, ("reason", "x"))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(gm, "/admin/players/987654321?handler=Ban", token, ("reason", "x"))).StatusCode);

        // Self-ban reopens dialog with error
        var selfBan = await PostAsync(gm, $"/admin/players/{gmId}?handler=Ban", token, ("reason", "x"));
        Assert.Equal(HttpStatusCode.OK, selfBan.StatusCode);
        string selfHtml = await selfBan.Content.ReadAsStringAsync();
        Assert.Contains("Kendi hesabını yasaklayamazsın.", selfHtml);
        Assert.Contains("id=\"dlg-ban\" class=\"open\"", selfHtml);

        // Service throws GameOfflineException when game server is offline
        var service = fixture.Factory.Services.GetRequiredService<PlayerService>();
        var gmPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, gmId.ToString()),
            new Claim(WebUser.AuthorityClaim, "3"),
        ], "test"));
        await Assert.ThrowsAsync<GameOfflineException>(() => service.BanAsync(playerId, "bot", gmPrincipal));
    }

    [Fact]
    public async Task FiltersHtmxPartialJailFilterAndSameIpWork()
    {
        uint jailedAcc = await this.CreateCharacterAsync(910101, "MahkumAli", cls: 15, mapId: Jail.MapID);
        uint freeAcc = await this.CreateCharacterAsync(910102, "OzgunTao", cls: 142, mapId: 1015);
        string altEmail = await fixture.CreateAccountAsync(authority: 1);
        uint altId = await this.AccountIdAsync(altEmail);

        await using (var db = new MySqlConnection(this.Cs(fixture.AccountSchema)))
        {
            await db.OpenAsync();
            await new MySqlCommand(
                "INSERT INTO logins (Timestamp, AccountID, IPAddress) VALUES " +
                $"(UTC_TIMESTAMP(), {freeAcc}, '198.51.100.77'), " +
                $"(UTC_TIMESTAMP(), {altId}, '198.51.100.77')", db).ExecuteNonQueryAsync();
        }

        var mod = await fixture.SignInAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");

        // In jail filter replaces PK jail tab
        string jailHtml = await mod.GetStringAsync("/admin/players?jailed=true");
        Assert.Contains("MahkumAli", jailHtml);
        Assert.Contains("Hapiste", jailHtml);
        Assert.DoesNotContain("OzgunTao", jailHtml);

        // Jailed character page shows release button and Grafana links
        string jailedDetail = await mod.GetStringAsync($"/admin/players/{jailedAcc}");
        Assert.Contains("Hapishaneden tahliye et", jailedDetail);
        Assert.Contains($"/grafana/d/fetih-player/player?var-env=dev&amp;var-characterId=910101&amp;var-accountId={jailedAcc}", jailedDetail);
        Assert.Contains("/grafana/d/fetih-chat/chat?", jailedDetail);

        // Release when game offline flashes offline error
        string token = await TokenFromAsync(mod, $"/admin/players/{jailedAcc}");
        var release = await PostAsync(mod, $"/admin/players/{jailedAcc}?handler=Release", token, ("characterId", "910101"));
        Assert.Equal(HttpStatusCode.Redirect, release.StatusCode);
        Assert.Contains("Oyun sunucusu kapalı; işlem yapılamadı.", await mod.GetStringAsync($"/admin/players/{jailedAcc}"));

        // htmx partial search returns fragment and sets HX-Replace-Url without handler=List
        var htmxResp = await mod.GetAsync("/admin/players?handler=List&q=OzgunTao");
        Assert.Equal(HttpStatusCode.OK, htmxResp.StatusCode);
        Assert.Equal("/admin/players?q=OzgunTao", htmxResp.Headers.GetValues("HX-Replace-Url").Single());
        string partial = await htmxResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<html", partial);
        Assert.Contains("OzgunTao", partial);
        Assert.DoesNotContain("MahkumAli", partial);

        // IP filter + Account/Logins tabs show shared IP
        string ipHtml = await mod.GetStringAsync("/admin/players?ip=198.51.100.77");
        Assert.Contains("OzgunTao", ipHtml);
        Assert.Contains(altEmail, ipHtml);

        string accountTab = await mod.GetStringAsync($"/admin/players/{freeAcc}?sekme=account");
        Assert.Contains(altEmail, accountTab);
        Assert.Contains("198.51.100.77", accountTab);

        string loginsTab = await mod.GetStringAsync($"/admin/players/{freeAcc}?sekme=logins");
        Assert.Contains("198.51.100.77", loginsTab);
    }

    [Fact]
    public async Task AdminChangesStatusAuthorityAndPasswordWithAudit()
    {
        string adminEmail = await fixture.CreateAccountAsync(authority: 5);
        string targetEmail = await fixture.CreateAccountAsync(authority: 1);
        uint targetId = await this.AccountIdAsync(targetEmail);

        var admin = await fixture.SignInAsync(adminEmail, "secret1");
        string token = await TokenFromAsync(admin, $"/admin/players/{targetId}");

        // Status=5 must use Ban
        var badStatus = await PostAsync(admin, $"/admin/players/{targetId}?handler=Status", token, ("statusId", "5"));
        Assert.Equal(HttpStatusCode.OK, badStatus.StatusCode);
        string badHtml = await badStatus.Content.ReadAsStringAsync();
        Assert.Contains("Yasakla işlemini kullan", badHtml);
        Assert.Contains("id=\"dlg-status\" class=\"open\"", badHtml);

        // Lock then activate
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(admin, $"/admin/players/{targetId}?handler=Status", token, ("statusId", "4"), ("reason", "review"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(admin, $"/admin/players/{targetId}?handler=Status", token, ("statusId", "2"))).StatusCode);

        // Promote to moderator
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(admin, $"/admin/players/{targetId}?handler=Authority", token, ("authorityId", "2"))).StatusCode);

        // Reset password and verify sign-in works
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(admin, $"/admin/players/{targetId}?handler=ResetPassword", token, ("newPassword", "newsecret9"))).StatusCode);
        var relogin = await fixture.SignInAsync(targetEmail, "newsecret9");
        Assert.Equal(HttpStatusCode.OK, (await relogin.GetAsync("/admin/players")).StatusCode);

        // Audit entries exist and password is not leaked
        await using var webDb = await fixture.Factory.Services
            .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Fetih.Web.Audit.WebDbContext>>()
            .CreateDbContextAsync();
        string targetIdStr = targetId.ToString();
        var entries = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            webDb.Audit.Where(e => e.Action.StartsWith("account.") && e.TargetID == targetIdStr));
        var actions = entries.Select(e => e.Action).ToList();
        Assert.Contains("account.set_status", actions);
        Assert.Contains("account.set_authority", actions);
        Assert.Contains("account.reset_password", actions);
        Assert.DoesNotContain(entries, e => (e.Summary ?? "").Contains("newsecret9"));
    }

    [Fact]
    public async Task BulkKickReportsFailuresAndOldLinksRedirect()
    {
        uint charAcc = await this.CreateCharacterAsync(910201, "TopluOyuncu");
        uint bareAcc = await this.AccountIdAsync(await fixture.CreateAccountAsync(authority: 1));

        var mod = await fixture.SignInAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");
        string token = await TokenFromAsync(mod, "/admin/players");

        var kick = await PostAsync(mod, "/admin/players?handler=BulkKick", token,
            ("accountIds", charAcc.ToString()),
            ("accountIds", bareAcc.ToString()),
            ("reason", "test"));
        Assert.Equal(HttpStatusCode.Redirect, kick.StatusCode);
        string listHtml = await mod.GetStringAsync("/admin/players");
        Assert.Contains("0 başarılı, 2 başarısız", listHtml);
        Assert.Contains("Oyun sunucusu kapalı", listHtml);
        Assert.Contains("Hesabın karakteri yok", listHtml);

        // Old redirects
        var r1 = await mod.GetAsync($"/admin/accounts/{charAcc}");
        Assert.Equal(HttpStatusCode.Redirect, r1.StatusCode);
        Assert.Equal($"/admin/players/{charAcc}", r1.Headers.Location?.OriginalString);

        var r2 = await mod.GetAsync("/admin/characters/910201");
        Assert.Equal(HttpStatusCode.Redirect, r2.StatusCode);
        Assert.Equal($"/admin/players/{charAcc}", r2.Headers.Location?.OriginalString);

        Assert.Equal(HttpStatusCode.NotFound, (await mod.GetAsync("/admin/characters/987654321")).StatusCode);
    }
}
