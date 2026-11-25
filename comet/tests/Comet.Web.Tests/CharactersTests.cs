namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MySqlConnector;
using Xunit;

[Collection(WebCollection.Name)]
public class CharactersTests(WebFixture fixture)
{
    private static readonly string DbHost = Environment.GetEnvironmentVariable("COMET_TEST_DB_HOST") is { Length: > 0 } h ? h : "localhost";
    private static readonly string DbUser = Environment.GetEnvironmentVariable("COMET_TEST_DB_USER") is { Length: > 0 } u ? u : "root";
    private static readonly string DbPassword = Environment.GetEnvironmentVariable("COMET_TEST_DB_PASSWORD") is { Length: > 0 } p ? p : "password";

    private string GameCs() => new MySqlConnectionStringBuilder
    {
        Server = DbHost, UserID = DbUser, Password = DbPassword, Database = fixture.GameSchema,
    }.ConnectionString;

    private string AccountCs() => new MySqlConnectionStringBuilder
    {
        Server = DbHost, UserID = DbUser, Password = DbPassword, Database = fixture.AccountSchema,
    }.ConnectionString;

    [Fact]
    public async Task PermissionsAreEnforcedOnCharacters()
    {
        var player = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/characters")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/characters/1")).StatusCode);

        uint target = await this.CharacterAsync(900201, "PermP1", authority: 1);
        uint adminCharacter = await this.CharacterAsync(900202, "PermAdm", authority: 5);

        string modEmail = await fixture.CreateAccountAsync(authority: 2);
        var mod = await fixture.LoginAsync(modEmail, "secret1");
        Assert.Equal(HttpStatusCode.OK, (await mod.GetAsync("/api/characters")).StatusCode);
        // Mod can run moderation actions (reaches GameRpc -> 503 offline), but not GM actions
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await mod.PostAsJsonAsync($"/api/characters/{target}/kick", new { reason = "test" })).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await mod.PostAsJsonAsync($"/api/characters/{target}/mute", new { minutes = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.PostAsJsonAsync($"/api/characters/{target}/give-item", new { typeId = 1088000, count = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.PostAsJsonAsync($"/api/characters/{target}/silver", new { amount = 1000 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.PostAsJsonAsync($"/api/characters/{target}/level", new { level = 50 })).StatusCode);
        // Nor on a character of a higher account, or of no account
        var outranked = await mod.PostAsJsonAsync($"/api/characters/{adminCharacter}/kick", new { reason = "test" });
        Assert.Equal(HttpStatusCode.Forbidden, outranked.StatusCode);
        Assert.Equal("outranked", (await outranked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await mod.PostAsJsonAsync("/api/characters/987654321/kick", new { reason = "test" })).StatusCode);

        string gmEmail = await fixture.CreateAccountAsync(authority: 3);
        var gm = await fixture.LoginAsync(gmEmail, "secret1");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await gm.PostAsJsonAsync($"/api/characters/{target}/give-item", new { typeId = 1088000, count = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await gm.PostAsJsonAsync($"/api/characters/{target}/silver", new { amount = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gm.PostAsJsonAsync($"/api/characters/{adminCharacter}/teleport", new { mapId = 1002, x = 430, y = 380 })).StatusCode);
    }

    // A character on a new account of the given authority
    private async Task<uint> CharacterAsync(uint id, string name, ushort authority)
    {
        string email = await fixture.CreateAccountAsync(authority: authority);
        uint accountId;
        await using (var adb = new MySqlConnection(this.AccountCs()))
        {
            await adb.OpenAsync();
            var cmd = new MySqlCommand("SELECT AccountID FROM account WHERE Username = @u", adb);
            cmd.Parameters.AddWithValue("@u", email);
            accountId = Convert.ToUInt32(await cmd.ExecuteScalarAsync());
        }
        await using var gdb = new MySqlConnection(this.GameCs());
        await gdb.OpenAsync();
        await new MySqlCommand(
            "INSERT INTO `character` (CharacterID, AccountID, Name, Level, CurrentClass, MapID, X, Y, Registered) VALUES " +
            $"({id}, {accountId}, '{name}', 50, 15, 1002, 430, 380, UTC_TIMESTAMP())", gdb).ExecuteNonQueryAsync();
        return id;
    }

    [Fact]
    public async Task ListAndDetailReturnItemsSkillsRelationsAndSocial()
    {
        string ownerEmail = await fixture.CreateAccountAsync(authority: 1);
        string friendEmail = await fixture.CreateAccountAsync(authority: 1);
        uint accountId, friendAccountId;
        await using (var adb = new MySqlConnection(this.AccountCs()))
        {
            await adb.OpenAsync();
            var cmd = new MySqlCommand("SELECT AccountID FROM account WHERE Username = @u", adb);
            cmd.Parameters.AddWithValue("@u", ownerEmail);
            accountId = Convert.ToUInt32(await cmd.ExecuteScalarAsync());
            cmd.Parameters["@u"].Value = friendEmail;
            friendAccountId = Convert.ToUInt32(await cmd.ExecuteScalarAsync());
        }

        uint charId = 900101;
        uint friendId = 900102;
        await using (var gdb = new MySqlConnection(this.GameCs()))
        {
            await gdb.OpenAsync();
            await new MySqlCommand(
                $"INSERT INTO `character` (CharacterID, AccountID, Name, Level, CurrentClass, MapID, X, Y, Silver, Jewels, Registered) VALUES " +
                $"({charId}, {accountId}, 'HeroP1', 120, 15, 1002, 430, 380, 50000, 250, UTC_TIMESTAMP()), " +
                $"({friendId}, {friendAccountId}, 'PalP1', 80, 45, 1002, 431, 381, 1000, 10, UTC_TIMESTAMP()); " +
                $"INSERT INTO item (ItemID, CharacterID, TypeID, Position, Amount, AmountLimit, Plus, Package) VALUES " +
                $"(8001, {charId}, 130009, 3, 1000, 1000, 6, 0), " +
                $"(8002, {charId}, 1088000, 0, 1, 1, 0, 0), " +
                $"(8003, {charId}, 1088001, 230, 1, 1, 0, 8); " +
                $"INSERT INTO magic (CharacterID, Type, Level, Experience, OldLevel) VALUES ({charId}, 1045, 4, 100, 0); " +
                $"INSERT INTO proficiency (CharacterID, Type, Level, Experience) VALUES ({charId}, 410, 12, 500); " +
                $"INSERT INTO friend (CharacterID, FriendID) VALUES ({charId}, {friendId}); " +
                $"INSERT INTO nobility (CharacterID, Donation) VALUES ({charId}, 30000000); " +
                $"INSERT INTO arena (CharacterID, Points, Day, WinsToday, LossesToday, WinsTotal, LossesTotal, HonorPoints, HonorTotal) VALUES ({charId}, 1200, 1, 3, 1, 20, 5, 100, 500);",
                gdb).ExecuteNonQueryAsync();
        }

        string modEmail = await fixture.CreateAccountAsync(authority: 2);
        var mod = await fixture.LoginAsync(modEmail, "secret1");

        var list = await mod.GetFromJsonAsync<JsonElement>("/api/characters?q=HeroP1&minLevel=100");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("HeroP1", list.GetProperty("items")[0].GetProperty("name").GetString());

        var detail = await mod.GetFromJsonAsync<JsonElement>($"/api/characters/{charId}");
        Assert.Equal("HeroP1", detail.GetProperty("name").GetString());
        Assert.Equal(ownerEmail, detail.GetProperty("accountUsername").GetString());
        Assert.Equal(1, detail.GetProperty("equipment").GetArrayLength());
        Assert.Equal(1, detail.GetProperty("inventory").GetArrayLength());
        Assert.Equal(1, detail.GetProperty("warehouses").GetArrayLength());
        Assert.Equal(8u, detail.GetProperty("warehouses")[0].GetProperty("packageId").GetUInt32());
        Assert.Equal(1, detail.GetProperty("magics").GetArrayLength());
        Assert.Equal(1, detail.GetProperty("proficiencies").GetArrayLength());
        Assert.Equal("PalP1", detail.GetProperty("friends")[0].GetProperty("name").GetString());
        Assert.Equal(30000000UL, detail.GetProperty("nobility").GetProperty("donation").GetUInt64());
        Assert.Equal(1200u, detail.GetProperty("arena").GetProperty("points").GetUInt32());
    }
}
