namespace Comet.Web.Features.Social;

using System.Globalization;
using Comet.Game.Database;
using Comet.Game.Flowers;
using Comet.Game.Nobility;
using Comet.Game.Social;
using Comet.Shared.Models.Admin;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Comet.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

// Guilds, families (clans in the game's code), nobility, arena, flowers, guild war and the
// message board. Views read the game database, which the game writes on every change;
// changes go through the game's RPC.
public class SocialFeature : IFeature
{
    public const int ListLimit = 200;
    private const byte RelationAlly = 1;

    // MsgTalk channels of the boards
    public static readonly IReadOnlyDictionary<ushort, string> BoardChannels = new Dictionary<ushort, string>
    {
        [2201] = "trade", [2202] = "friend", [2203] = "team", [2204] = "guild", [2205] = "others",
    };

    public void Map(RouteGroupBuilder api)
    {
        var social = api.MapGroup("/social").RequirePermission(Permission.AdminArea);
        social.MapGet("/guilds", GuildsAsync).WithName("socialGuilds");
        social.MapGet("/guilds/{id}", GuildAsync).WithName("socialGuild");
        social.MapGet("/families", FamiliesAsync).WithName("socialFamilies");
        social.MapGet("/families/{id}", FamilyAsync).WithName("socialFamily");
        social.MapGet("/nobility", NobilityAsync).WithName("socialNobility");
        social.MapGet("/arena", ArenaAsync).WithName("socialArena");
        social.MapGet("/arena/seasons/{season}", ArenaSeasonAsync).WithName("socialArenaSeason");
        social.MapGet("/flowers", FlowersAsync).WithName("socialFlowers");
        social.MapGet("/guildwar", GuildWarAsync).WithName("socialGuildWar");
        social.MapGet("/board", BoardAsync).WithName("socialBoard");

        social.MapPost("/guildwar/start", (GameRpc game, AuditLog audit, HttpContext http) =>
                GuildWarActionAsync(game, audit, http, "StartGuildWar", "guild_war.start"))
            .WithName("socialStartGuildWar").RequirePermission(Permission.ManageCharacters);
        social.MapPost("/guildwar/end", (GameRpc game, AuditLog audit, HttpContext http) =>
                GuildWarActionAsync(game, audit, http, "EndGuildWar", "guild_war.end"))
            .WithName("socialEndGuildWar").RequirePermission(Permission.ManageCharacters);
        social.MapPost("/guilds/{id}/disband", DisbandAsync)
            .WithName("socialDisbandGuild").RequirePermission(Permission.ManageCharacters);
        social.MapDelete("/board/{channel}/{characterId}", DeletePostAsync)
            .WithName("socialDeletePost").RequirePermission(Permission.Moderate);
    }

    private static async Task<GuildSummary[]> GuildsAsync(string? search, CancellationToken ct)
    {
        await using var db = new ServerDbContext();
        var query = db.Syndicates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(g => g.Name.Contains(search.Trim()));
        var guilds = await query.OrderByDescending(g => g.Fund).Take(ListLimit).ToListAsync(ct);
        var ids = guilds.Select(g => g.SyndicateID).ToList();
        var members = await db.SyndicateMembers.AsNoTracking().Where(m => ids.Contains(m.SyndicateID))
            .GroupBy(m => m.SyndicateID).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var leaders = await CharactersAsync(db, guilds.Select(g => g.LeaderID), ct);
        return guilds.Select(g => new GuildSummary(g.SyndicateID, g.Name, leaders.GetValueOrDefault(g.LeaderID),
            g.Level, g.Fund, g.ConquerPointsFund, members.GetValueOrDefault(g.SyndicateID), Day(g.Created))).ToArray();
    }

    private static async Task<Results<Ok<GuildDetail>, NotFound<ApiError>>> GuildAsync(uint id, CancellationToken ct)
    {
        await using var db = new ServerDbContext();
        var guild = await db.Syndicates.AsNoTracking().SingleOrDefaultAsync(g => g.SyndicateID == id, ct);
        if (guild == null)
            return TypedResults.NotFound(new ApiError("not_found"));
        var members = await db.SyndicateMembers.AsNoTracking().Where(m => m.SyndicateID == id).ToListAsync(ct);
        var relations = await db.SyndicateRelations.AsNoTracking().Where(r => r.SyndicateID == id).ToListAsync(ct);
        var characters = await CharactersAsync(db, members.Select(m => m.CharacterID).Append(guild.LeaderID), ct);
        var names = await db.Syndicates.AsNoTracking()
            .Where(g => relations.Select(r => r.TargetID).Contains(g.SyndicateID))
            .ToDictionaryAsync(g => g.SyndicateID, g => g.Name, ct);
        GuildRef[] Related(bool ally) => relations.Where(r => (r.Kind == RelationAlly) == ally)
            .Select(r => new GuildRef(r.TargetID, names.GetValueOrDefault(r.TargetID) ?? $"#{r.TargetID}")).ToArray();

        var summary = new GuildSummary(guild.SyndicateID, guild.Name, characters.GetValueOrDefault(guild.LeaderID),
            guild.Level, guild.Fund, guild.ConquerPointsFund, members.Count, Day(guild.Created));
        var views = members
            .Select(m => new GuildMemberView(characters.GetValueOrDefault(m.CharacterID) ?? Unknown(m.CharacterID),
                m.Rank, m.SilverDonation, m.ConquerPointsDonation, Day(m.Joined)))
            .OrderByDescending(m => m.Rank).ThenByDescending(m => m.SilverDonation).ToArray();
        return TypedResults.Ok(new GuildDetail(summary, guild.Announcement ?? "", guild.RequiredLevel,
            guild.RequiredRebirths, views, Related(true), Related(false)));
    }

    private static async Task<FamilySummary[]> FamiliesAsync(string? search, CancellationToken ct)
    {
        await using var db = new ServerDbContext();
        var query = db.Families.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(f => f.Name.Contains(search.Trim()));
        var families = await query.OrderByDescending(f => f.Fund).Take(ListLimit).ToListAsync(ct);
        var ids = families.Select(f => f.FamilyID).ToList();
        var members = await db.FamilyMembers.AsNoTracking().Where(m => ids.Contains(m.FamilyID))
            .GroupBy(m => m.FamilyID).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var territories = await db.FamilyOccupations.AsNoTracking().Where(o => ids.Contains(o.FamilyID))
            .GroupBy(o => o.FamilyID).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var leaders = await CharactersAsync(db, families.Select(f => f.LeaderID), ct);
        return families.Select(f => new FamilySummary(f.FamilyID, f.Name, leaders.GetValueOrDefault(f.LeaderID),
            f.Level, f.Fund, members.GetValueOrDefault(f.FamilyID), Day(f.Created),
            territories.GetValueOrDefault(f.FamilyID))).ToArray();
    }

    private static async Task<Results<Ok<FamilyDetail>, NotFound<ApiError>>> FamilyAsync(uint id, CancellationToken ct)
    {
        await using var db = new ServerDbContext();
        var family = await db.Families.AsNoTracking().SingleOrDefaultAsync(f => f.FamilyID == id, ct);
        if (family == null)
            return TypedResults.NotFound(new ApiError("not_found"));
        var members = await db.FamilyMembers.AsNoTracking().Where(m => m.FamilyID == id).ToListAsync(ct);
        var relations = await db.FamilyRelations.AsNoTracking().Where(r => r.FamilyID == id).ToListAsync(ct);
        var occupied = await db.FamilyOccupations.AsNoTracking().Where(o => o.FamilyID == id).ToListAsync(ct);
        var signups = await db.FamilyWarSignups.AsNoTracking().Where(s => s.FamilyID == id).ToListAsync(ct);
        var characters = await CharactersAsync(db, members.Select(m => m.CharacterID).Append(family.LeaderID), ct);
        var names = await db.Families.AsNoTracking()
            .Where(f => relations.Select(r => r.TargetID).Contains(f.FamilyID))
            .ToDictionaryAsync(f => f.FamilyID, f => f.Name, ct);
        GuildRef[] Related(bool ally) => relations.Where(r => (r.Kind == RelationAlly) == ally)
            .Select(r => new GuildRef(r.TargetID, names.GetValueOrDefault(r.TargetID) ?? $"#{r.TargetID}")).ToArray();
        static string Territory(uint npc) => ClanWar.Find(npc)?.Name ?? $"#{npc}";

        var summary = new FamilySummary(family.FamilyID, family.Name, characters.GetValueOrDefault(family.LeaderID),
            family.Level, family.Fund, members.Count, Day(family.Created), occupied.Count);
        var views = members
            .Select(m => new FamilyMemberView(characters.GetValueOrDefault(m.CharacterID) ?? Unknown(m.CharacterID),
                m.Rank, m.Donation, Day(m.Joined)))
            .OrderByDescending(m => m.Rank).ThenByDescending(m => m.Donation).ToArray();
        return TypedResults.Ok(new FamilyDetail(summary, family.Announcement ?? "", views, Related(true), Related(false),
            occupied.Select(o => new FamilyTerritory(o.NpcID, Territory(o.NpcID), o.Days, o.RevenueClaimed)).ToArray(),
            signups.Select(s => new FamilyTerritory(s.NpcID, Territory(s.NpcID), 0, false)).ToArray()));
    }

    // The same order and ranks the game shows: top 50 on the board, then by donation
    private static async Task<NobilityEntry[]> NobilityAsync(CancellationToken ct)
    {
        await using var db = new ServerDbContext();
        var rows = await db.Nobility.AsNoTracking().Where(n => n.Donation > 0).ToListAsync(ct);
        var ordered = NobilityBoard.Order(rows.Select(r => new Donor(r.CharacterID, "", 0, r.Donation)));
        var standings = NobilityBoard.Rank(ordered);
        var top = ordered.Take(ListLimit).ToList();
        var characters = await CharactersAsync(db, top.Select(d => d.ID), ct);
        return top.Select((d, i) => new NobilityEntry(i + 1, characters.GetValueOrDefault(d.ID) ?? Unknown(d.ID),
            d.Donation, standings[d.ID].Rank.ToString())).ToArray();
    }

    private static async Task<ArenaResponse> ArenaAsync(CancellationToken ct)
    {
        await using var db = new ServerDbContext();
        var rows = await db.Arena.AsNoTracking()
            .Where(a => a.Points > 0 || a.WinsTotal > 0 || a.LossesTotal > 0)
            .OrderByDescending(a => a.Points).ThenByDescending(a => a.WinsTotal).ThenBy(a => a.CharacterID)
            .Take(ListLimit).ToListAsync(ct);
        var characters = await CharactersAsync(db, rows.Select(r => r.CharacterID), ct);
        var seasons = await db.ArenaSeasons.AsNoTracking().Select(s => s.Season).Distinct()
            .OrderByDescending(s => s).ToArrayAsync(ct);
        return new ArenaResponse(rows.Select((a, i) => new ArenaEntry(i + 1,
            characters.GetValueOrDefault(a.CharacterID) ?? Unknown(a.CharacterID), a.Points, a.WinsToday, a.LossesToday,
            a.WinsTotal, a.LossesTotal, a.HonorPoints, a.HonorTotal)).ToArray(), seasons);
    }

    private static async Task<ArenaSeasonEntry[]> ArenaSeasonAsync(uint season, CancellationToken ct)
    {
        await using var db = new ServerDbContext();
        return await db.ArenaSeasons.AsNoTracking().Where(s => s.Season == season).OrderBy(s => s.Rank)
            .Select(s => new ArenaSeasonEntry(s.Rank, s.CharacterID, s.Name, s.Level, s.Class, s.Points, s.Wins, s.Losses))
            .ToArrayAsync(ct);
    }

    private static async Task<FlowerEntryView[]> FlowersAsync(FlowerKind? kind, CancellationToken ct)
    {
        var flower = kind ?? FlowerKind.RedRose;
        await using var db = new ServerDbContext();
        var rows = await db.Flowers.AsNoTracking().ToListAsync(ct);
        (uint Total, uint Today) Of(Comet.Game.Database.Models.DbFlower f) => flower switch
        {
            FlowerKind.Lily => (f.Lilies, f.LiliesToday),
            FlowerKind.Orchid => (f.Orchids, f.OrchidsToday),
            FlowerKind.Tulip => (f.Tulips, f.TulipsToday),
            _ => (f.RedRoses, f.RedRosesToday),
        };
        var top = rows.Where(f => Of(f).Total > 0)
            .OrderByDescending(f => Of(f).Total).ThenBy(f => f.CharacterID).Take(ListLimit).ToList();
        var characters = await CharactersAsync(db, top.Select(f => f.CharacterID), ct);
        return top.Select((f, i) => new FlowerEntryView(i + 1,
            characters.GetValueOrDefault(f.CharacterID) ?? Unknown(f.CharacterID), Of(f).Total, Of(f).Today)).ToArray();
    }

    private static async Task<GuildWarResponse> GuildWarAsync(GameRpc game, CancellationToken ct)
    {
        GuildWarState? live = null;
        if (game.Connected)
        {
            try { live = await game.CallAsync<GuildWarState>("GuildWarStatus"); }
            catch (GameOfflineException) { }
        }
        await using var db = new ServerDbContext();
        var saved = await db.GuildWar.AsNoTracking().FirstOrDefaultAsync(ct);
        var ids = new[] { saved?.WinnerGuildID ?? 0, saved?.HolderGuildID ?? 0, live?.HolderGuildID ?? 0, live?.WinnerGuildID ?? 0 }
            .Where(i => i != 0).Distinct().ToList();
        var names = await db.Syndicates.AsNoTracking().Where(g => ids.Contains(g.SyndicateID))
            .ToDictionaryAsync(g => g.SyndicateID, g => g.Name, ct);
        GuildRef? Ref(uint? guildId) => guildId is uint g and not 0
            ? new GuildRef(g, names.GetValueOrDefault(g) ?? $"#{g}") : null;
        return new GuildWarResponse(live,
            new GuildWarSaved(Ref(saved?.WinnerGuildID), Ref(saved?.HolderGuildID), saved?.RewardClaimed ?? false,
                saved?.WonAt is DateTime wonAt ? DateTime.SpecifyKind(wonAt, DateTimeKind.Utc) : null),
            Ref(live?.HolderGuildID), Ref(live?.WinnerGuildID));
    }

    private static async Task<BoardPost[]> BoardAsync(ushort? channel, CancellationToken ct)
    {
        await using var db = new ServerDbContext();
        var query = db.MessageBoard.AsNoTracking();
        if (channel is ushort c)
            query = query.Where(p => p.Channel == c);
        var posts = await query.OrderByDescending(p => p.PostedAt).Take(ListLimit).ToListAsync(ct);
        var characters = await CharactersAsync(db, posts.Select(p => p.CharacterID), ct);
        var guildIds = posts.Select(p => p.SyndicateID).Where(i => i != 0).Distinct().ToList();
        var guilds = await db.Syndicates.AsNoTracking().Where(g => guildIds.Contains(g.SyndicateID))
            .ToDictionaryAsync(g => g.SyndicateID, g => g.Name, ct);
        return posts.Select(p => new BoardPost(BoardChannels.GetValueOrDefault(p.Channel) ?? p.Channel.ToString(CultureInfo.InvariantCulture),
            p.Channel, p.CharacterID, characters.GetValueOrDefault(p.CharacterID), p.AuthorName,
            p.SyndicateID != 0 ? new GuildRef(p.SyndicateID, guilds.GetValueOrDefault(p.SyndicateID) ?? $"#{p.SyndicateID}") : null,
            p.Message, DateTime.SpecifyKind(p.PostedAt, DateTimeKind.Utc))).ToArray();
    }

    private static async Task<Results<Ok<AdminResult>, Conflict<ApiError>>> GuildWarActionAsync(
        GameRpc game, AuditLog audit, HttpContext http, string method, string action)
    {
        var result = await game.CallAsync<AdminResult>(method, new AdminArgs { ActorAccountID = http.User.AccountId() });
        if (!result.Ok)
            return TypedResults.Conflict(new ApiError(action == "guild_war.start" ? "already_running" : "not_running"));
        await audit.WriteAsync(action, "guild_war", "1", summary: action == "guild_war.start" ? "Started" : "Ended");
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<AdminResult>, NotFound<ApiError>>> DisbandAsync(
        uint id, GameRpc game, AuditLog audit, HttpContext http, CancellationToken ct)
    {
        GuildSummary? before;
        await using (var db = new ServerDbContext())
        {
            var guild = await db.Syndicates.AsNoTracking().SingleOrDefaultAsync(g => g.SyndicateID == id, ct);
            if (guild == null)
                return TypedResults.NotFound(new ApiError("not_found"));
            before = new GuildSummary(guild.SyndicateID, guild.Name, null, guild.Level, guild.Fund,
                guild.ConquerPointsFund, await db.SyndicateMembers.CountAsync(m => m.SyndicateID == id, ct), Day(guild.Created));
        }
        var result = await game.CallAsync<AdminResult>("DisbandGuild",
            new GuildArgs { ActorAccountID = http.User.AccountId(), GuildID = id });
        if (!result.Ok)
            return TypedResults.NotFound(new ApiError("not_found"));
        await audit.WriteAsync("guild.disband", "guild", id.ToString(CultureInfo.InvariantCulture), before: before,
            summary: $"Disbanded {before.Name}");
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<AdminResult>, NotFound<ApiError>>> DeletePostAsync(
        ushort channel, uint characterId, GameRpc game, AuditLog audit, HttpContext http, CancellationToken ct)
    {
        Comet.Game.Database.Models.DbMessageBoard? post;
        await using (var db = new ServerDbContext())
            post = await db.MessageBoard.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Channel == channel && p.CharacterID == characterId, ct);
        if (post == null || !BoardChannels.ContainsKey(channel))
            return TypedResults.NotFound(new ApiError("not_found"));
        var result = await game.CallAsync<AdminResult>("DeleteBoardPost",
            new BoardPostArgs { ActorAccountID = http.User.AccountId(), Channel = channel, CharacterID = characterId });
        if (!result.Ok)
            return TypedResults.NotFound(new ApiError("not_found"));
        await audit.WriteAsync("board.delete_post", "character", characterId.ToString(CultureInfo.InvariantCulture),
            before: new { post.Channel, post.AuthorName, post.Message, post.PostedAt },
            summary: $"Deleted {post.AuthorName}'s {BoardChannels[channel]} post");
        return TypedResults.Ok(result);
    }

    private static async Task<Dictionary<uint, CharacterRef>> CharactersAsync(
        ServerDbContext db, IEnumerable<uint> ids, CancellationToken ct)
    {
        var wanted = ids.Where(i => i != 0).Distinct().ToList();
        return await db.Characters.AsNoTracking().Where(c => wanted.Contains(c.CharacterID))
            .Select(c => new CharacterRef(c.CharacterID, c.Name, c.Level, c.CurrentClass))
            .ToDictionaryAsync(c => c.Id, ct);
    }

    private static CharacterRef Unknown(uint id) => new(id, $"#{id}", 0, 0);

    // Guilds and families keep days as yyyyMMdd
    private static DateOnly? Day(uint value) =>
        DateOnly.TryParseExact(value.ToString(CultureInfo.InvariantCulture), "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var day) ? day : null;
}
