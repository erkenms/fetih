namespace Fetih.Web.Tests;

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;

[Collection(WebCollection.Name)]
public class PublicPagesTests(WebFixture fixture)
{
    private async Task SeedAsync()
    {
        var host = Environment.GetEnvironmentVariable("FETIH_TEST_DB_HOST") is { Length: > 0 } h ? h : "localhost";
        var user = Environment.GetEnvironmentVariable("FETIH_TEST_DB_USER") is { Length: > 0 } u ? u : "root";
        var pass = Environment.GetEnvironmentVariable("FETIH_TEST_DB_PASSWORD") is { Length: > 0 } p ? p : "password";
        var connStr = new MySqlConnectionStringBuilder
        {
            Server = host,
            UserID = user,
            Password = pass,
            Database = fixture.GameSchema,
        }.ConnectionString;

        await using var db = new MySqlConnection(connStr);
        await db.OpenAsync();

        var sql = """
            DELETE FROM `subclass`;
            DELETE FROM `family_occupy`;
            DELETE FROM `family_member`;
            DELETE FROM `family`;
            DELETE FROM `arena_season`;
            DELETE FROM `arena`;
            DELETE FROM `flower`;
            DELETE FROM `nobility`;
            DELETE FROM `guild_war`;
            DELETE FROM `syndicate_relation`;
            DELETE FROM `syndicate_member`;
            DELETE FROM `syndicate`;
            DELETE FROM `character`;

            INSERT INTO `character`
                (CharacterID, AccountID, Name, Spouse, Mesh, Avatar, Hairstyle, Silver, Jewels, StoredSilver,
                 CurrentClass, PreviousClass, AncestorClass, Rebirths, FirstLifeLevel, SecondLifeLevel,
                 Level, Experience, MapID, X, Y, Virtue, Strength, Agility, Vitality, Spirit,
                 AttributePoints, HealthPoints, ManaPoints, KillPoints, EnlightenPoints, QuizPoints,
                 VIPLevel, SecondPassword, ActiveSubClass, Registered)
            VALUES
                (2001, 1, 'Alp', 'Bilge', 1003, 1, 100, 50000, 200, 10000,
                 15, 10, 0, 2, 130, 130, 135, 999999, 1002, 430, 380, 100, 100, 50, 50, 10,
                 0, 5000, 500, 25, 0, 42, 6, 123456, 1, '2026-01-01 00:00:00'),
                (2002, 2, 'Bilge', 'Alp', 1004, 2, 101, 1000, 0, 0,
                 135, 132, 100, 1, 120, 0, 130, 50000, 1002, 400, 400, 50, 10, 10, 20, 200,
                 0, 3000, 3000, 0, 0, 10, 1, 0, 0, '2026-01-02 00:00:00');

            INSERT INTO `syndicate`
                (SyndicateID, Name, LeaderID, Fund, ConquerPointsFund, Announcement, AnnouncementDate, Level, Created)
            VALUES
                (110, 'Kartallar', 2001, 5000000, 1000, 'Birlikte güçlüyüz', 20260101, 5, 1735689600),
                (120, 'Aslanlar', 2002, 2000000, 500, 'İleri', 20260102, 3, 1735776000);

            INSERT INTO `syndicate_member` (CharacterID, SyndicateID, Rank, SilverDonation, ConquerPointsDonation, Joined)
            VALUES
                (2001, 110, 1000, 1000000, 500, 1735689600),
                (2002, 120, 1000, 500000, 100, 1735776000);

            INSERT INTO `syndicate_relation` (SyndicateID, TargetID, Kind)
            VALUES (110, 120, 1);

            INSERT INTO `guild_war` (ID, WinnerGuildID, HolderGuildID, RewardClaimed, WonAt)
            VALUES (1, 110, 110, 0, '2026-01-10 20:00:00');

            INSERT INTO `nobility` (CharacterID, Donation)
            VALUES (2001, 500000000);

            INSERT INTO `arena` (CharacterID, Points, Day, WinsToday, LossesToday, WinsTotal, LossesTotal, HonorPoints, HonorTotal)
            VALUES (2001, 2200, 20260110, 5, 1, 50, 10, 1000, 5000);

            INSERT INTO `arena_season` (Season, `Rank`, CharacterID, Name, Mesh, Level, Class, Points, Wins, Losses)
            VALUES (1, 1, 2001, 'Alp', 1003, 135, 15, 2500, 40, 5);

            INSERT INTO `flower` (CharacterID, RedRoses, RedRosesToday, Lilies, LiliesToday, Orchids, OrchidsToday, Tulips, TulipsToday)
            VALUES (2002, 500, 10, 250, 5, 100, 2, 50, 1);

            INSERT INTO `family` (FamilyID, Name, LeaderID, Fund, Level, Announcement, Created)
            VALUES (11, 'Bozkurtlar', 2001, 900000, 4, 'Selam', 1735689600);

            INSERT INTO `family_member` (CharacterID, FamilyID, Rank, Donation, Joined)
            VALUES (2001, 11, 100, 500000, 1735689600);

            INSERT INTO `subclass` (CharacterID, SubClassID, Level, Phase)
            VALUES (2001, 1, 9, 9);
            """;

        await new MySqlCommand(sql, db).ExecuteNonQueryAsync();

        if (fixture.Factory.Services.GetRequiredService<IMemoryCache>() is MemoryCache mem)
            mem.Compact(1.0);
    }

    [Fact]
    public async Task EveryPublicPageAnswersSignedOut()
    {
        await this.SeedAsync();
        var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        (string Url, string Expected)[] pages =
        [
            ("/", "Hoş Geldin"),
            ("/?eniyi=soyluluk", "Kral"),
            ("/?eniyi=lonca", "Kartallar"),
            ("/?eniyi=arena", "Alp"),
            ("/siralama", "Alp"),
            ("/siralama/seviye?sinif=100", "Bilge"),
            ("/siralama/soyluluk", "Kral"),
            ("/siralama/lonca?sirala=uye", "Kartallar"),
            ("/siralama/arena", "Alp"),
            ("/siralama/arena?mod=onur", "Alp"),
            ("/siralama/arena?mod=sezon&sezon=1", "Alp"),
            ("/siralama/cicek?cicek=zambak", "Bilge"),
            ("/siralama/pk", "Alp"),
            ("/siralama/aile", "Bozkurtlar"),
            ("/karakter/2001", "Dövüş Ustası"),
            ("/lonca/110", "Kartallar"),
            ("/haberler", "Depo geçmişinden son değişiklikler."),
            ("/indir", "oyun istemcisini indir"),
            ("/kayit", "E-posta adresi"),
        ];

        foreach (var (url, expected) in pages)
        {
            var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} returned {response.StatusCode}");
            string html = await response.Content.ReadAsStringAsync();
            Assert.Contains(expected, html);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/karakter/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/lonca/999999")).StatusCode);

        var accountRedirect = await client.GetAsync("/hesabim");
        Assert.Equal(HttpStatusCode.Redirect, accountRedirect.StatusCode);
        Assert.Equal("/giris?donus=%2Fhesabim", accountRedirect.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task RegistrationCreatesAccountAndRejectsTakenName()
    {
        string email = $"page{Guid.NewGuid():N}"[..12] + "@test.local";
        var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var mismatch = await WebFixture.PostFormAsync(client, "/kayit", new()
        {
            ["Eposta"] = email,
            ["Sifre"] = "secret1",
            ["SifreTekrar"] = "secret2",
        });
        Assert.Equal(HttpStatusCode.OK, mismatch.StatusCode);
        Assert.Contains("Şifreler eşleşmiyor.", await mismatch.Content.ReadAsStringAsync());

        var created = await WebFixture.PostFormAsync(client, "/kayit", new()
        {
            ["Eposta"] = email,
            ["Sifre"] = "secret1",
            ["SifreTekrar"] = "secret1",
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal("/hesabim", created.Headers.Location?.OriginalString);

        string accountHtml = await client.GetStringAsync("/hesabim");
        Assert.Contains(email, accountHtml);
        Assert.Contains("Hesabın oluşturuldu.", accountHtml);

        // Fresh signed-out client trying the same email gets rejected
        var other = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var taken = await WebFixture.PostFormAsync(other, "/kayit", new()
        {
            ["Eposta"] = email.ToUpperInvariant(),
            ["Sifre"] = "secret1",
            ["SifreTekrar"] = "secret1",
        });
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
        Assert.Contains("Bu e-posta adresiyle kayıtlı bir hesap zaten var.", await taken.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ChangingPasswordRequiresTheOldOne()
    {
        string email = await fixture.CreateAccountAsync(authority: 1);
        var client = await fixture.SignInAsync(email, "secret1");

        var wrong = await WebFixture.PostFormAsync(client, "/hesabim?handler=Password", new()
        {
            ["mevcutSifre"] = "wrongpw",
            ["yeniSifre"] = "secret2",
            ["yeniSifreTekrar"] = "secret2",
        });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Mevcut şifre hatalı.", await wrong.Content.ReadAsStringAsync());

        var ok = await WebFixture.PostFormAsync(client, "/hesabim?handler=Password", new()
        {
            ["mevcutSifre"] = "secret1",
            ["yeniSifre"] = "secret2",
            ["yeniSifreTekrar"] = "secret2",
        });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Contains("Şifren güncellendi.", await client.GetStringAsync("/hesabim"));

        // Old password no longer signs in; new password does
        var fresh = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var oldLogin = await WebFixture.PostFormAsync(fresh, "/giris", new()
        {
            ["kullanici"] = email,
            ["sifre"] = "secret1",
        });
        Assert.Equal(HttpStatusCode.OK, oldLogin.StatusCode);
        Assert.Contains("Hesap adı ya da şifre yanlış.", await oldLogin.Content.ReadAsStringAsync());

        var newLogin = await WebFixture.PostFormAsync(fresh, "/giris", new()
        {
            ["kullanici"] = email,
            ["sifre"] = "secret2",
        });
        Assert.Equal(HttpStatusCode.Redirect, newLogin.StatusCode);
    }

    [Fact]
    public async Task GuildDisbandRequiresGameMaster()
    {
        await this.SeedAsync();

        // Signed out doesn't see Disband and cannot post
        var anon = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string anonHtml = await anon.GetStringAsync("/lonca/110");
        Assert.DoesNotContain("Loncayı Dağıt", anonHtml);

        // Regular player doesn't see Disband and gets 403 on POST
        var player = await fixture.SignInAsync("test", "test");
        string playerHtml = await player.GetStringAsync("/lonca/110");
        Assert.DoesNotContain("Loncayı Dağıt", playerHtml);

        string girisPage = await player.GetStringAsync("/giris");
        string token = System.Text.RegularExpressions.Regex.Match(girisPage,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var playerPost = await player.PostAsync("/lonca/110?handler=Disband", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Forbidden, playerPost.StatusCode);

        // A moderator doesn't see Disband either and gets 403
        string modEmail = await fixture.CreateAccountAsync(authority: 2);
        var moderator = await fixture.SignInAsync(modEmail, "secret1");
        Assert.DoesNotContain("Loncayı Dağıt", await moderator.GetStringAsync("/lonca/110"));
        var moderatorPost = await WebFixture.PostFormAsync(moderator, "/lonca/110?handler=Disband", new());
        Assert.Equal(HttpStatusCode.Forbidden, moderatorPost.StatusCode);

        // A game master sees Disband and POST reaches the handler (game offline in tests -> flash redirect)
        string gmEmail = await fixture.CreateAccountAsync(authority: 3);
        var mod = await fixture.SignInAsync(gmEmail, "secret1");
        string modHtml = await mod.GetStringAsync("/lonca/110");
        Assert.Contains("Loncayı Dağıt", modHtml);

        var modPost = await WebFixture.PostFormAsync(mod, "/lonca/110?handler=Disband", new());
        Assert.Equal(HttpStatusCode.Redirect, modPost.StatusCode);
        Assert.Contains("Oyun sunucusu kapalı; işlem yapılamadı.", await mod.GetStringAsync("/lonca/110"));
    }
}
