namespace Fetih.Web.Tests;

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using Xunit;

[Collection(WebCollection.Name)]
public class BoardPageTests(WebFixture fixture)
{
    private async Task ExecuteAsync(string sql)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Environment.GetEnvironmentVariable("FETIH_TEST_DB_HOST") is { Length: > 0 } host ? host : "localhost",
            UserID = Environment.GetEnvironmentVariable("FETIH_TEST_DB_USER") is { Length: > 0 } user ? user : "root",
            Password = Environment.GetEnvironmentVariable("FETIH_TEST_DB_PASSWORD") is { Length: > 0 } pw ? pw : "password",
            Database = fixture.GameSchema,
        };
        await using var db = new MySqlConnection(builder.ConnectionString);
        await db.OpenAsync();
        await new MySqlCommand(sql, db).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task SignedOutGoesToSignIn()
    {
        var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/admin/board");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/giris?donus=%2Fadmin%2Fboard", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task PlayerIsForbiddenFromBoardAndDelete()
    {
        var client = await fixture.SignInAsync(WebFixture.Player, WebFixture.PlayerPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/board")).StatusCode);

        string page = await client.GetStringAsync("/giris");
        string token = Regex.Match(page,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var post = await client.PostAsync("/admin/board?handler=Delete", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["channel"] = "2201",
                ["characterId"] = "1",
                ["__RequestVerificationToken"] = token,
            }));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task ModeratorListsPostsByChannelAndDeletesThroughRpc()
    {
        await ExecuteAsync("""
            INSERT INTO message_board (Channel, CharacterID, SyndicateID, AuthorName, Message, PostedAt) VALUES
              (2201, 7500001, 0, 'Tuccar', 'Satılık elmas', '2026-11-24 10:00:00'),
              (2202, 7500002, 0, 'Dost', 'Arkadaş arıyorum', '2026-11-25 12:00:00');
            """);

        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email, "secret1");

        string all = await client.GetStringAsync("/admin/board");
        Assert.Contains("Satılık elmas", all);
        Assert.Contains("Arkadaş arıyorum", all);
        // Newest post appears before the older one
        Assert.True(all.IndexOf("Arkadaş arıyorum", StringComparison.Ordinal) <
                    all.IndexOf("Satılık elmas", StringComparison.Ordinal));

        string trade = await client.GetStringAsync("/admin/board?channel=2201");
        Assert.Contains("Satılık elmas", trade);
        Assert.DoesNotContain("Arkadaş arıyorum", trade);

        var missing = await WebFixture.PostFormAsync(client, "/admin/board?handler=Delete", new()
        {
            ["channel"] = "2201",
            ["characterId"] = "7599999",
            ["currentChannel"] = "2201",
        });
        Assert.Equal(HttpStatusCode.Redirect, missing.StatusCode);
        Assert.Equal("/admin/board?channel=2201", missing.Headers.Location?.ToString());
        Assert.Contains("İleti bulunamadı; silinmiş olabilir.", await client.GetStringAsync("/admin/board?channel=2201"));

        var offline = await WebFixture.PostFormAsync(client, "/admin/board?handler=Delete", new()
        {
            ["channel"] = "2201",
            ["characterId"] = "7500001",
        });
        Assert.Equal(HttpStatusCode.Redirect, offline.StatusCode);
        Assert.Contains("Oyun sunucusu kapalı; işlem yapılamadı.", await client.GetStringAsync("/admin/board"));
    }
}
