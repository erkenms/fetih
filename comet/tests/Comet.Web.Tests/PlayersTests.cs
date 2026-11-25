namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MySqlConnector;
using Xunit;

[Collection(WebCollection.Name)]
public class PlayersTests(WebFixture fixture)
{
    private static readonly string DbHost = Environment.GetEnvironmentVariable("COMET_TEST_DB_HOST") is { Length: > 0 } h ? h : "localhost";
    private static readonly string DbUser = Environment.GetEnvironmentVariable("COMET_TEST_DB_USER") is { Length: > 0 } u ? u : "root";
    private static readonly string DbPassword = Environment.GetEnvironmentVariable("COMET_TEST_DB_PASSWORD") is { Length: > 0 } p ? p : "password";

    private string Cs(string schema) => new MySqlConnectionStringBuilder
    {
        Server = DbHost, UserID = DbUser, Password = DbPassword, Database = schema,
    }.ConnectionString;

    private async Task<uint> AccountIdAsync(string email)
    {
        await using var db = new MySqlConnection(this.Cs(fixture.AccountSchema));
        await db.OpenAsync();
        var cmd = new MySqlCommand("SELECT AccountID FROM account WHERE Username = @u", db);
        cmd.Parameters.AddWithValue("@u", email);
        return Convert.ToUInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<uint> CharacterAsync(uint id, string name, byte cls, uint mapId)
    {
        uint accountId = await this.AccountIdAsync(await fixture.CreateAccountAsync(authority: 1));
        await using var db = new MySqlConnection(this.Cs(fixture.GameSchema));
        await db.OpenAsync();
        await new MySqlCommand(
            "INSERT INTO `character` (CharacterID, AccountID, Name, Level, CurrentClass, MapID, X, Y, Registered) VALUES " +
            $"({id}, {accountId}, '{name}', 70, {cls}, {mapId}, 100, 100, UTC_TIMESTAMP())", db).ExecuteNonQueryAsync();
        return accountId;
    }

    [Fact]
    public async Task ListJoinsAccountsAndCharactersAndFilters()
    {
        uint taoist = await this.CharacterAsync(900301, "PlyTao", 142, 1015);
        uint trojan = await this.CharacterAsync(900302, "PlyTro", 15, 1015);
        string bareEmail = await fixture.CreateAccountAsync(authority: 1);
        uint bare = await this.AccountIdAsync(bareEmail);

        var mod = await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");

        var byName = await mod.GetFromJsonAsync<JsonElement>("/api/players?q=PlyTao");
        Assert.Equal(1, byName.GetProperty("total").GetInt32());
        Assert.Equal(taoist, byName.GetProperty("items")[0].GetProperty("accountId").GetUInt32());

        var taoists = await mod.GetFromJsonAsync<JsonElement>("/api/players?q=Ply&classLine=10&mapId=1015");
        Assert.Equal([taoist], taoists.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("accountId").GetUInt32()));

        // An account with no character is listed, but not once a character filter is set
        var noChar = await mod.GetFromJsonAsync<JsonElement>($"/api/players?q={Uri.EscapeDataString(bareEmail)}");
        Assert.Equal(1, noChar.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, noChar.GetProperty("items")[0].GetProperty("characterId").ValueKind);
        var noCharFiltered = await mod.GetFromJsonAsync<JsonElement>($"/api/players?q={Uri.EscapeDataString(bareEmail)}&minLevel=1");
        Assert.Equal(0, noCharFiltered.GetProperty("total").GetInt32());

        // With no game server, nobody is online
        var online = await mod.GetFromJsonAsync<JsonElement>("/api/players?online=true");
        Assert.Equal(0, online.GetProperty("total").GetInt32());

        var ids = await mod.GetFromJsonAsync<JsonElement>("/api/players/ids?q=Ply&mapId=1015");
        Assert.Equal(new[] { taoist, trojan }.Order(), ids.GetProperty("accountIds").EnumerateArray().Select(i => i.GetUInt32()).Order());
        _ = bare;
    }

    [Fact]
    public async Task BulkChecksPermissionsAndReportsEachFailure()
    {
        uint target = await this.CharacterAsync(900311, "PlyBulk", 15, 1002);
        uint bare = await this.AccountIdAsync(await fixture.CreateAccountAsync(authority: 1));

        var player = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsJsonAsync("/api/players/bulk", new { action = "kick", accountIds = new[] { target } })).StatusCode);

        var mod = await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.PostAsJsonAsync("/api/players/bulk", new { action = "ban", accountIds = new[] { target } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await mod.PostAsJsonAsync("/api/players/bulk", new { action = "nuke", accountIds = new[] { target } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await mod.PostAsJsonAsync("/api/players/bulk", new { action = "kick", accountIds = Array.Empty<uint>() })).StatusCode);

        var kick = await mod.PostAsJsonAsync("/api/players/bulk", new { action = "kick", accountIds = new[] { target, bare } });
        Assert.Equal(HttpStatusCode.OK, kick.StatusCode);
        var body = await kick.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("succeeded").GetInt32());
        var errors = body.GetProperty("failures").EnumerateArray()
            .ToDictionary(f => f.GetProperty("accountId").GetUInt32(), f => f.GetProperty("error").GetString());
        Assert.Equal("game_offline", errors[target]);
        Assert.Equal("no_character", errors[bare]);
    }
}
