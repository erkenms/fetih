namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using Comet.Web.Features.PublicSite;
using MySqlConnector;
using Xunit;

[Collection(WebCollection.Name)]
public class PublicSiteTests(WebFixture fixture)
{
    private async Task SeedAsync()
    {
        var host = Environment.GetEnvironmentVariable("COMET_TEST_DB_HOST") is { Length: > 0 } h ? h : "localhost";
        var user = Environment.GetEnvironmentVariable("COMET_TEST_DB_USER") is { Length: > 0 } u ? u : "root";
        var pass = Environment.GetEnvironmentVariable("COMET_TEST_DB_PASSWORD") is { Length: > 0 } p ? p : "password";
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
                (1001, 1, 'Alice', 'Bob', 1003, 1, 100, 50000, 200, 10000,
                 15, 10, 0, 2, 130, 130, 135, 999999, 1002, 430, 380, 100, 100, 50, 50, 10,
                 0, 5000, 500, 25, 0, 42, 6, 123456, 1, '2026-01-01 00:00:00'),
                (1002, 2, 'Bob', 'Alice', 1004, 2, 101, 1000, 0, 0,
                 135, 132, 100, 1, 120, 0, 130, 50000, 1002, 400, 400, 50, 10, 10, 20, 200,
                 0, 3000, 3000, 0, 0, 10, 1, 0, 0, '2026-01-02 00:00:00'),
                (1003, 3, 'Carol', 'None', 2003, 3, 200, 0, 0, 0,
                 45, 0, 0, 0, 0, 0, 125, 1000, 1002, 410, 410, 0, 50, 150, 30, 0,
                 0, 2500, 200, 120, 0, 5, 0, 0, 0, '2026-01-03 00:00:00');

            INSERT INTO `syndicate`
                (SyndicateID, Name, LeaderID, Fund, ConquerPointsFund, Announcement, AnnouncementDate, Level, Created)
            VALUES
                (10, 'Dragons', 1001, 5000000, 1000, 'Welcome Dragons', 20260101, 5, 1735689600),
                (20, 'Phoenix', 1002, 2000000, 500, 'Rise up', 20260102, 3, 1735776000);

            INSERT INTO `syndicate_member` (CharacterID, SyndicateID, Rank, SilverDonation, ConquerPointsDonation, Joined)
            VALUES
                (1001, 10, 1000, 1000000, 500, 1735689600),
                (1003, 10, 200, 100000, 0, 1735776000),
                (1002, 20, 1000, 500000, 100, 1735776000);

            INSERT INTO `syndicate_relation` (SyndicateID, TargetID, Kind)
            VALUES (10, 20, 1);

            INSERT INTO `guild_war` (ID, WinnerGuildID, HolderGuildID, RewardClaimed, WonAt)
            VALUES (1, 10, 10, 0, '2026-01-10 20:00:00');

            INSERT INTO `nobility` (CharacterID, Donation)
            VALUES
                (1001, 500000000),
                (1002, 200000000);

            INSERT INTO `arena` (CharacterID, Points, Day, WinsToday, LossesToday, WinsTotal, LossesTotal, HonorPoints, HonorTotal)
            VALUES
                (1001, 2200, 20260110, 5, 1, 50, 10, 1000, 5000),
                (1002, 1800, 20260110, 3, 2, 30, 20, 2000, 9000);

            INSERT INTO `arena_season` (Season, `Rank`, CharacterID, Name, Mesh, Level, Class, Points, Wins, Losses)
            VALUES
                (1, 1, 1001, 'Alice', 1003, 135, 15, 2500, 40, 5),
                (1, 2, 1002, 'Bob', 1004, 130, 135, 2100, 30, 12);

            INSERT INTO `flower` (CharacterID, RedRoses, RedRosesToday, Lilies, LiliesToday, Orchids, OrchidsToday, Tulips, TulipsToday)
            VALUES
                (1003, 500, 10, 250, 5, 100, 2, 50, 1);

            INSERT INTO `family` (FamilyID, Name, LeaderID, Fund, Level, Announcement, Created)
            VALUES
                (1, 'Starks', 1001, 900000, 4, 'Winter is coming', 1735689600);

            INSERT INTO `family_member` (CharacterID, FamilyID, Rank, Donation, Joined)
            VALUES
                (1001, 1, 100, 500000, 1735689600);

            INSERT INTO `family_occupy` (NpcID, FamilyID, Days, RevenueClaimed)
            VALUES
                (10001, 1, 3, 0);

            INSERT INTO `subclass` (CharacterID, SubClassID, Level, Phase)
            VALUES
                (1001, 1, 9, 9);
            """;

        await new MySqlCommand(sql, db).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task PublicSiteEndpointsWorkAndProtectPrivateFields()
    {
        await SeedAsync();
        using var client = fixture.Factory.CreateClient();

        // Home
        var home = await client.GetFromJsonAsync<PublicHomeResponse>("/api/public/home");
        Assert.NotNull(home);
        Assert.False(home.GameOnline);
        Assert.Null(home.OnlinePlayers);
        Assert.NotNull(home.GuildWar);
        Assert.Equal("Dragons", home.GuildWar.HolderGuildName);
        Assert.Equal("Alice", home.TopPlayers[0].Name);
        Assert.Equal("King", home.TopNobility[0].NobilityRank);
        Assert.Equal("Dragons", home.TopGuilds[0].Name);
        Assert.Equal("Alice", home.TopArena[0].Name);

        // Level ranking + class filter (100 = Taoist -> Bob 135)
        var levelAll = await client.GetFromJsonAsync<RankingPage<LevelRankEntry>>("/api/public/rankings/level");
        Assert.NotNull(levelAll);
        Assert.Equal(3, levelAll.Total);
        Assert.Equal("Alice", levelAll.Items[0].Name);

        var levelTao = await client.GetFromJsonAsync<RankingPage<LevelRankEntry>>("/api/public/rankings/level?baseClass=100");
        Assert.NotNull(levelTao);
        Assert.Equal(1, levelTao.Total);
        Assert.Equal("Bob", levelTao.Items[0].Name);

        // Nobility
        var nobility = await client.GetFromJsonAsync<RankingPage<NobilityRankEntry>>("/api/public/rankings/nobility");
        Assert.NotNull(nobility);
        Assert.Equal(2, nobility.Total);
        Assert.Equal("King", nobility.Items[0].NobilityRank);
        Assert.Equal("King", nobility.Items[1].NobilityRank);

        // Guilds sort by members (Dragons has 2, Phoenix has 1)
        var guilds = await client.GetFromJsonAsync<RankingPage<GuildRankEntry>>("/api/public/rankings/guilds?sort=members");
        Assert.NotNull(guilds);
        Assert.Equal(2, guilds.Total);
        Assert.Equal("Dragons", guilds.Items[0].Name);
        Assert.Equal(2, guilds.Items[0].MemberCount);
        Assert.True(guilds.Items[0].IsGuildWarHolder);

        // Arena points & honor & seasons
        var arenaHonor = await client.GetFromJsonAsync<RankingPage<ArenaRankEntry>>("/api/public/rankings/arena?kind=honor");
        Assert.NotNull(arenaHonor);
        Assert.Equal("Bob", arenaHonor.Items[0].Name);

        var seasons = await client.GetFromJsonAsync<ArenaSeasonInfo[]>("/api/public/rankings/arena/seasons");
        Assert.NotNull(seasons);
        Assert.Single(seasons);
        Assert.Equal(1u, seasons[0].Season);

        var season1 = await client.GetFromJsonAsync<RankingPage<ArenaSeasonRankEntry>>("/api/public/rankings/arena/seasons/1");
        Assert.NotNull(season1);
        Assert.Equal(2, season1.Total);
        Assert.Equal("Alice", season1.Items[0].Name);

        // Flowers
        var flowers = await client.GetFromJsonAsync<RankingPage<FlowerRankEntry>>("/api/public/rankings/flowers?kind=lily");
        Assert.NotNull(flowers);
        Assert.Single(flowers.Items);
        Assert.Equal("Carol", flowers.Items[0].Name);
        Assert.Equal(250u, flowers.Items[0].Amount);

        // PK
        var pk = await client.GetFromJsonAsync<RankingPage<PkRankEntry>>("/api/public/rankings/pk");
        Assert.NotNull(pk);
        Assert.Equal(2, pk.Total);
        Assert.Equal("Carol", pk.Items[0].Name);
        Assert.Equal(120, pk.Items[0].PkPoints);

        // Families
        var families = await client.GetFromJsonAsync<RankingPage<FamilyRankEntry>>("/api/public/rankings/families");
        Assert.NotNull(families);
        Assert.Single(families.Items);
        Assert.Equal("Starks", families.Items[0].Name);
        Assert.Equal(1, families.Items[0].OccupiedMaps);

        // Public Character (and privacy check on raw JSON)
        var charJson = await client.GetStringAsync("/api/public/characters/1001");
        Assert.DoesNotContain("accountId", charJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("silver", charJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jewels", charJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secondPassword", charJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mapId", charJson, StringComparison.OrdinalIgnoreCase);

        var ch = await client.GetFromJsonAsync<PublicCharacterResponse>("/api/public/characters/1001");
        Assert.NotNull(ch);
        Assert.Equal("Alice", ch.Name);
        Assert.Equal("Dragons", ch.Guild?.Name);
        Assert.Equal("Starks", ch.Family?.Name);
        Assert.Equal("King", ch.Nobility?.Rank);
        Assert.Single(ch.SubClasses);

        var missingChar = await client.GetAsync("/api/public/characters/999999");
        Assert.Equal(HttpStatusCode.NotFound, missingChar.StatusCode);

        // Public Guild
        var guild = await client.GetFromJsonAsync<PublicGuildResponse>("/api/public/guilds/10");
        Assert.NotNull(guild);
        Assert.Equal("Dragons", guild.Name);
        Assert.Single(guild.Allies);
        Assert.Equal("Phoenix", guild.Allies[0].Name);
        Assert.Equal(2, guild.Members.Length);

        var missingGuild = await client.GetAsync("/api/public/guilds/999999");
        Assert.Equal(HttpStatusCode.NotFound, missingGuild.StatusCode);

        // News & Download
        var news = await client.GetFromJsonAsync<NewsPage>("/api/public/news");
        Assert.NotNull(news);

        var download = await client.GetFromJsonAsync<DownloadInfoResponse>("/api/public/download");
        Assert.NotNull(download);
        Assert.Equal("5619", download.Version);
    }
}
