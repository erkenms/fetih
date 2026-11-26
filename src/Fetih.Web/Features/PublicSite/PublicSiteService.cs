namespace Fetih.Web.Features.PublicSite;

using System.Globalization;
using System.Security.Claims;
using Fetih.Game.Database;
using Fetih.Game.Nobility;
using Fetih.Shared.Models.Admin;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Fetih.Web.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

public sealed class PublicSiteService(
    IMemoryCache cache,
    GameRpc rpc,
    GitNewsService newsService,
    IConfiguration configuration,
    AuditLog audit)
{
    public const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private static readonly TimeSpan RankingCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StatusCacheDuration = TimeSpan.FromSeconds(15);

    public bool GameConnected => rpc.Connected;

    public async Task<PublicHomeResponse> GetHomeAsync(CancellationToken ct = default)
    {
        var (online, onlineCount) = await this.GetOnlineStatusAsync();
        var homeData = await cache.GetOrCreateAsync("public:home", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RankingCacheDuration;
            await using var db = new ServerDbContext();

            var gw = await GetGuildWarSummaryAsync(db);
            var topPlayers = (await QueryLevelRankingAsync(db, null, 1, 10)).Items;
            var topNobility = (await BuildNobilityBoardAsync(db)).Entries.Take(10).ToArray();
            var topGuilds = (await QueryGuildRankingAsync(db, "fund", 1, 10)).Items;
            var topArena = (await QueryArenaRankingAsync(db, "points", 1, 10)).Items;
            var latestNews = (await newsService.GetNewsAsync(ct)).Take(5).ToArray();

            return new HomeSnapshot(gw, topPlayers, topNobility, topGuilds, topArena, latestNews);
        });

        return new PublicHomeResponse(
            online,
            onlineCount,
            homeData!.GuildWar,
            homeData.TopPlayers,
            homeData.TopNobility,
            homeData.TopGuilds,
            homeData.TopArena,
            homeData.LatestNews);
    }

    public async Task<(bool Online, int? Players)> GetOnlineStatusAsync() =>
        await cache.GetOrCreateAsync("public:status", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = StatusCacheDuration;
            if (!rpc.Connected)
                return (false, (int?)null);
            try
            {
                var status = await rpc.CallAsync<ServerState>("ServerStatus");
                return (true, (int?)status.Players);
            }
            catch
            {
                return (false, (int?)null);
            }
        });

    public async Task<RankingPage<LevelRankEntry>> GetLevelRankingAsync(
        int? baseClass = null, int page = 1, int pageSize = DefaultPageSize)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        string key = $"public:rank:level:{baseClass?.ToString() ?? "all"}:{page}:{pageSize}";
        var result = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RankingCacheDuration;
            await using var db = new ServerDbContext();
            return await QueryLevelRankingAsync(db, baseClass, page, pageSize);
        });
        return result!;
    }

    public async Task<RankingPage<NobilityRankEntry>> GetNobilityRankingAsync(
        int page = 1, int pageSize = DefaultPageSize)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var board = await GetCachedNobilityBoardAsync(cache);
        var items = board.Entries.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return new RankingPage<NobilityRankEntry>(items, board.Entries.Length, page, pageSize, board.CachedAt);
    }

    public async Task<RankingPage<GuildRankEntry>> GetGuildRankingAsync(
        string sort = "fund", int page = 1, int pageSize = DefaultPageSize)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        string sortKey = sort == "members" ? "members" : "fund";
        string key = $"public:rank:guilds:{sortKey}:{page}:{pageSize}";
        var result = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RankingCacheDuration;
            await using var db = new ServerDbContext();
            return await QueryGuildRankingAsync(db, sortKey, page, pageSize);
        });
        return result!;
    }

    public async Task<RankingPage<ArenaRankEntry>> GetArenaRankingAsync(
        string kind = "points", int page = 1, int pageSize = DefaultPageSize)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        string kindKey = kind == "honor" ? "honor" : "points";
        string key = $"public:rank:arena:{kindKey}:{page}:{pageSize}";
        var result = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RankingCacheDuration;
            await using var db = new ServerDbContext();
            return await QueryArenaRankingAsync(db, kindKey, page, pageSize);
        });
        return result!;
    }

    public async Task<ArenaSeasonInfo[]> GetArenaSeasonsAsync()
    {
        var seasons = await cache.GetOrCreateAsync("public:rank:arena:seasons", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RankingCacheDuration;
            await using var db = new ServerDbContext();
            return await db.ArenaSeasons.AsNoTracking()
                .GroupBy(s => s.Season)
                .OrderByDescending(g => g.Key)
                .Select(g => new ArenaSeasonInfo(g.Key, g.Count()))
                .ToArrayAsync();
        });
        return seasons!;
    }

    public async Task<RankingPage<ArenaSeasonRankEntry>> GetArenaSeasonRankingAsync(
        uint season, int page = 1, int pageSize = DefaultPageSize)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        string key = $"public:rank:arena:season:{season}:{page}:{pageSize}";
        var result = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RankingCacheDuration;
            await using var db = new ServerDbContext();
            var query = db.ArenaSeasons.AsNoTracking().Where(s => s.Season == season);
            int total = await query.CountAsync();
            var items = await query
                .OrderBy(s => s.Rank)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(s => new ArenaSeasonRankEntry(s.Rank, s.CharacterID, s.Name, s.Level, s.Class, s.Points, s.Wins, s.Losses))
                .ToArrayAsync();
            return new RankingPage<ArenaSeasonRankEntry>(items, total, page, pageSize, DateTime.UtcNow);
        });
        return result!;
    }

    public async Task<RankingPage<FlowerRankEntry>> GetFlowerRankingAsync(
        string kind = "redRose", int page = 1, int pageSize = DefaultPageSize)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        string kindKey = kind switch
        {
            "lily" or "orchid" or "tulip" or "total" => kind,
            _ => "redRose"
        };
        string key = $"public:rank:flowers:{kindKey}:{page}:{pageSize}";
        var result = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RankingCacheDuration;
            await using var db = new ServerDbContext();
            var joined = from f in db.Flowers.AsNoTracking()
                         join c in db.Characters.AsNoTracking() on f.CharacterID equals c.CharacterID
                         select new { f, c.Name, c.Avatar, c.Level, c.CurrentClass };

            var filtered = kindKey switch
            {
                "lily" => joined.Where(x => x.f.Lilies > 0).OrderByDescending(x => x.f.Lilies).ThenBy(x => x.f.CharacterID),
                "orchid" => joined.Where(x => x.f.Orchids > 0).OrderByDescending(x => x.f.Orchids).ThenBy(x => x.f.CharacterID),
                "tulip" => joined.Where(x => x.f.Tulips > 0).OrderByDescending(x => x.f.Tulips).ThenBy(x => x.f.CharacterID),
                "total" => joined.Where(x => x.f.RedRoses + x.f.Lilies + x.f.Orchids + x.f.Tulips > 0)
                    .OrderByDescending(x => (ulong)x.f.RedRoses + x.f.Lilies + x.f.Orchids + x.f.Tulips)
                    .ThenBy(x => x.f.CharacterID),
                _ => joined.Where(x => x.f.RedRoses > 0).OrderByDescending(x => x.f.RedRoses).ThenBy(x => x.f.CharacterID),
            };

            int total = await filtered.CountAsync();
            var rows = await filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
            int offset = (page - 1) * pageSize;
            var items = rows.Select((x, idx) =>
            {
                uint amount = kindKey switch
                {
                    "lily" => x.f.Lilies,
                    "orchid" => x.f.Orchids,
                    "tulip" => x.f.Tulips,
                    "total" => (uint)Math.Min((ulong)x.f.RedRoses + x.f.Lilies + x.f.Orchids + x.f.Tulips, uint.MaxValue),
                    _ => x.f.RedRoses,
                };
                return new FlowerRankEntry(offset + idx + 1, x.f.CharacterID, x.Name, x.Avatar, x.Level, x.CurrentClass,
                    amount, x.f.RedRoses, x.f.Lilies, x.f.Orchids, x.f.Tulips);
            }).ToArray();
            return new RankingPage<FlowerRankEntry>(items, total, page, pageSize, DateTime.UtcNow);
        });
        return result!;
    }

    public async Task<RankingPage<PkRankEntry>> GetPkRankingAsync(
        int page = 1, int pageSize = DefaultPageSize)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        string key = $"public:rank:pk:{page}:{pageSize}";
        var result = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RankingCacheDuration;
            await using var db = new ServerDbContext();
            var query = db.Characters.AsNoTracking().Where(c => c.KillPoints > 0);
            int total = await query.CountAsync();
            var rows = await (
                from c in query
                join sm in db.SyndicateMembers.AsNoTracking() on c.CharacterID equals sm.CharacterID into smg
                from sm in smg.DefaultIfEmpty()
                join s in db.Syndicates.AsNoTracking() on sm.SyndicateID equals s.SyndicateID into sg
                from s in sg.DefaultIfEmpty()
                orderby c.KillPoints descending, c.Level descending, c.CharacterID ascending
                select new
                {
                    c.CharacterID,
                    c.Name,
                    c.Avatar,
                    c.Level,
                    c.Rebirths,
                    c.CurrentClass,
                    c.KillPoints,
                    GuildId = (uint?)s.SyndicateID,
                    GuildName = s != null ? s.Name : null,
                })
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToArrayAsync();

            int offset = (page - 1) * pageSize;
            var items = rows.Select((x, idx) => new PkRankEntry(
                offset + idx + 1, x.CharacterID, x.Name, x.Avatar, x.Level, x.Rebirths, x.CurrentClass,
                x.KillPoints, x.GuildId, x.GuildName)).ToArray();
            return new RankingPage<PkRankEntry>(items, total, page, pageSize, DateTime.UtcNow);
        });
        return result!;
    }

    public async Task<RankingPage<FamilyRankEntry>> GetFamilyRankingAsync(
        int page = 1, int pageSize = DefaultPageSize)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        string key = $"public:rank:families:{page}:{pageSize}";
        var result = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RankingCacheDuration;
            await using var db = new ServerDbContext();
            int total = await db.Families.AsNoTracking().CountAsync();
            var rows = await (
                from f in db.Families.AsNoTracking()
                join leader in db.Characters.AsNoTracking() on f.LeaderID equals leader.CharacterID into lg
                from leader in lg.DefaultIfEmpty()
                orderby f.Fund descending, f.Level descending, f.FamilyID ascending
                select new
                {
                    f.FamilyID,
                    f.Name,
                    f.Level,
                    f.LeaderID,
                    LeaderName = leader != null ? leader.Name : null,
                    f.Fund,
                    MemberCount = db.FamilyMembers.Count(m => m.FamilyID == f.FamilyID),
                    OccupiedMaps = db.FamilyOccupations.Count(o => o.FamilyID == f.FamilyID),
                })
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToArrayAsync();

            int offset = (page - 1) * pageSize;
            var items = rows.Select((x, idx) => new FamilyRankEntry(
                offset + idx + 1, x.FamilyID, x.Name, x.Level, x.LeaderID, x.LeaderName,
                x.MemberCount, x.Fund, x.OccupiedMaps)).ToArray();
            return new RankingPage<FamilyRankEntry>(items, total, page, pageSize, DateTime.UtcNow);
        });
        return result!;
    }

    public async Task<PublicCharacterResponse?> GetCharacterAsync(uint id)
    {
        await using var db = new ServerDbContext();
        var ch = await db.Characters.AsNoTracking().FirstOrDefaultAsync(c => c.CharacterID == id);
        if (ch == null)
            return null;

        var guild = await (
            from sm in db.SyndicateMembers.AsNoTracking()
            join s in db.Syndicates.AsNoTracking() on sm.SyndicateID equals s.SyndicateID
            where sm.CharacterID == id
            select new PublicCharacterGuild(s.SyndicateID, s.Name, sm.Rank))
            .FirstOrDefaultAsync();

        var family = await (
            from fm in db.FamilyMembers.AsNoTracking()
            join f in db.Families.AsNoTracking() on fm.FamilyID equals f.FamilyID
            where fm.CharacterID == id
            select new PublicCharacterFamily(f.FamilyID, f.Name, fm.Rank))
            .FirstOrDefaultAsync();

        var board = await GetCachedNobilityBoardAsync(cache);
        PublicCharacterNobility? nobility = null;
        if (board.Standings.TryGetValue(id, out var standing))
        {
            ulong donation = board.Donations.TryGetValue(id, out var d) ? d : 0;
            nobility = new PublicCharacterNobility(
                standing.Rank.ToString(),
                standing.Position >= 0 ? standing.Position + 1 : null,
                donation);
        }

        var fl = await db.Flowers.AsNoTracking().FirstOrDefaultAsync(f => f.CharacterID == id);
        var flowers = fl == null ? null : new PublicCharacterFlowers(fl.RedRoses, fl.Lilies, fl.Orchids, fl.Tulips);

        var ar = await db.Arena.AsNoTracking().FirstOrDefaultAsync(a => a.CharacterID == id);
        var arena = ar == null ? null : new PublicCharacterArena(ar.Points, ar.HonorTotal, ar.WinsTotal, ar.LossesTotal);

        var subs = await db.SubClasses.AsNoTracking()
            .Where(s => s.CharacterID == id)
            .OrderBy(s => s.SubClassID)
            .Select(s => new PublicCharacterSubClass(s.SubClassID, s.Level, s.Phase, s.SubClassID == ch.ActiveSubClass))
            .ToArrayAsync();

        string? spouse = string.IsNullOrWhiteSpace(ch.Spouse) || ch.Spouse == "None" ? null : ch.Spouse;

        return new PublicCharacterResponse(
            ch.CharacterID,
            ch.Name,
            ch.Avatar,
            ch.Mesh,
            ch.Level,
            ch.Rebirths,
            ch.CurrentClass,
            ch.PreviousClass,
            ch.AncestorClass,
            spouse,
            ch.KillPoints,
            ch.Virtue,
            ch.QuizPoints,
            DateTime.SpecifyKind(ch.Registered, DateTimeKind.Utc),
            guild,
            family,
            nobility,
            flowers,
            arena,
            subs);
    }

    public async Task<PublicGuildResponse?> GetGuildAsync(uint id)
    {
        await using var db = new ServerDbContext();
        var g = await db.Syndicates.AsNoTracking().FirstOrDefaultAsync(s => s.SyndicateID == id);
        if (g == null)
            return null;

        var leaderName = await db.Characters.AsNoTracking()
            .Where(c => c.CharacterID == g.LeaderID)
            .Select(c => c.Name)
            .FirstOrDefaultAsync();

        var gw = await db.GuildWar.AsNoTracking().FirstOrDefaultAsync();
        bool isHolder = gw != null && gw.HolderGuildID == id;
        bool isWinner = gw != null && gw.WinnerGuildID == id;

        var relations = await (
            from r in db.SyndicateRelations.AsNoTracking()
            join t in db.Syndicates.AsNoTracking() on r.TargetID equals t.SyndicateID
            where r.SyndicateID == id
            orderby t.Name
            select new { r.Kind, t.SyndicateID, t.Name })
            .ToArrayAsync();

        var allies = relations.Where(r => r.Kind == 1).Select(r => new PublicGuildRelation(r.SyndicateID, r.Name)).ToArray();
        var enemies = relations.Where(r => r.Kind == 2).Select(r => new PublicGuildRelation(r.SyndicateID, r.Name)).ToArray();

        var members = await (
            from m in db.SyndicateMembers.AsNoTracking()
            join c in db.Characters.AsNoTracking() on m.CharacterID equals c.CharacterID
            where m.SyndicateID == id
            orderby m.Rank descending, c.Level descending, c.Name ascending
            select new PublicGuildMember(c.CharacterID, c.Name, c.Avatar, c.Level, c.CurrentClass, m.Rank, m.Joined))
            .ToArrayAsync();

        return new PublicGuildResponse(
            g.SyndicateID,
            g.Name,
            g.Level,
            g.LeaderID,
            leaderName,
            string.IsNullOrWhiteSpace(g.Announcement) ? null : g.Announcement,
            g.Fund,
            g.ConquerPointsFund,
            g.Created,
            isHolder,
            isWinner,
            allies,
            enemies,
            members);
    }

    public async Task<string?> DisbandGuildAsync(uint id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        object? before;
        string name;
        await using (var db = new ServerDbContext())
        {
            var guild = await db.Syndicates.AsNoTracking().SingleOrDefaultAsync(g => g.SyndicateID == id, ct);
            if (guild == null)
                return "not_found";
            int memberCount = await db.SyndicateMembers.CountAsync(m => m.SyndicateID == id, ct);
            name = guild.Name;
            before = new { guild.SyndicateID, guild.Name, guild.Level, guild.Fund, guild.ConquerPointsFund, memberCount };
        }

        var result = await rpc.CallAsync<AdminResult>("DisbandGuild",
            new GuildArgs { ActorAccountID = user.AccountId(), GuildID = id });
        if (!result.Ok)
            return "not_found";

        await audit.WriteAsync("guild.disband", "guild", id.ToString(CultureInfo.InvariantCulture),
            before: before, summary: $"Disbanded {name}");
        cache.Remove("public:home");
        return null;
    }

    public async Task<NewsPage> GetNewsAsync(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var all = await newsService.GetNewsAsync(ct);
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return new NewsPage(items, all.Length);
    }

    public DownloadInfoResponse GetDownloadInfo()
    {
        string? url = configuration["Web:ClientDownloadUrl"];
        if (string.IsNullOrWhiteSpace(url))
            url = null;
        return new DownloadInfoResponse(url, "5619");
    }

    internal static async Task<NobilityBoardCache> GetCachedNobilityBoardAsync(IMemoryCache cache)
    {
        var board = await cache.GetOrCreateAsync("public:rank:nobility:board", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RankingCacheDuration;
            await using var db = new ServerDbContext();
            return await BuildNobilityBoardAsync(db);
        });
        return board!;
    }

    private static (int Page, int PageSize) NormalizePage(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));

    private static async Task<GuildWarSummary?> GetGuildWarSummaryAsync(ServerDbContext db)
    {
        var gw = await db.GuildWar.AsNoTracking().FirstOrDefaultAsync();
        if (gw == null || (gw.HolderGuildID == 0 && gw.WinnerGuildID == 0))
            return null;

        uint? holderId = gw.HolderGuildID == 0 ? null : gw.HolderGuildID;
        uint? winnerId = gw.WinnerGuildID == 0 ? null : gw.WinnerGuildID;
        string? holderName = holderId == null
            ? null
            : await db.Syndicates.AsNoTracking().Where(s => s.SyndicateID == holderId).Select(s => s.Name).FirstOrDefaultAsync();
        string? winnerName = winnerId == null
            ? null
            : await db.Syndicates.AsNoTracking().Where(s => s.SyndicateID == winnerId).Select(s => s.Name).FirstOrDefaultAsync();

        DateTime? wonAt = gw.WonAt.HasValue ? DateTime.SpecifyKind(gw.WonAt.Value, DateTimeKind.Utc) : null;
        return new GuildWarSummary(holderId, holderName, winnerId, winnerName, wonAt);
    }

    private static async Task<RankingPage<LevelRankEntry>> QueryLevelRankingAsync(
        ServerDbContext db, int? baseClass, int page, int pageSize)
    {
        var query = db.Characters.AsNoTracking().AsQueryable();
        if (baseClass.HasValue)
        {
            int min = baseClass.Value;
            int max = min == 100 ? 149 : min + 9;
            query = query.Where(c => c.CurrentClass >= min && c.CurrentClass <= max);
        }

        int total = await query.CountAsync();
        var rows = await (
            from c in query
            join sm in db.SyndicateMembers.AsNoTracking() on c.CharacterID equals sm.CharacterID into smg
            from sm in smg.DefaultIfEmpty()
            join s in db.Syndicates.AsNoTracking() on sm.SyndicateID equals s.SyndicateID into sg
            from s in sg.DefaultIfEmpty()
            orderby c.Rebirths descending, c.Level descending, c.Experience descending, c.CharacterID ascending
            select new
            {
                c.CharacterID,
                c.Name,
                c.Avatar,
                c.Level,
                c.Rebirths,
                c.CurrentClass,
                GuildId = (uint?)s.SyndicateID,
                GuildName = s != null ? s.Name : null,
            })
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToArrayAsync();

        int offset = (page - 1) * pageSize;
        var items = rows.Select((x, idx) => new LevelRankEntry(
            offset + idx + 1, x.CharacterID, x.Name, x.Avatar, x.Level, x.Rebirths, x.CurrentClass,
            x.GuildId, x.GuildName)).ToArray();
        return new RankingPage<LevelRankEntry>(items, total, page, pageSize, DateTime.UtcNow);
    }

    private static async Task<RankingPage<GuildRankEntry>> QueryGuildRankingAsync(
        ServerDbContext db, string sort, int page, int pageSize)
    {
        var gw = await db.GuildWar.AsNoTracking().FirstOrDefaultAsync();
        uint holderId = gw?.HolderGuildID ?? 0;
        uint winnerId = gw?.WinnerGuildID ?? 0;

        int total = await db.Syndicates.AsNoTracking().CountAsync();
        var baseQuery =
            from s in db.Syndicates.AsNoTracking()
            join leader in db.Characters.AsNoTracking() on s.LeaderID equals leader.CharacterID into lg
            from leader in lg.DefaultIfEmpty()
            select new
            {
                s.SyndicateID,
                s.Name,
                s.Level,
                s.LeaderID,
                LeaderName = leader != null ? leader.Name : null,
                MemberCount = db.SyndicateMembers.Count(m => m.SyndicateID == s.SyndicateID),
                s.Fund,
                s.ConquerPointsFund,
            };

        var ordered = sort == "members"
            ? baseQuery.OrderByDescending(x => x.MemberCount).ThenByDescending(x => x.Fund).ThenBy(x => x.SyndicateID)
            : baseQuery.OrderByDescending(x => x.Fund).ThenByDescending(x => x.MemberCount).ThenBy(x => x.SyndicateID);

        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        int offset = (page - 1) * pageSize;
        var items = rows.Select((x, idx) => new GuildRankEntry(
            offset + idx + 1,
            x.SyndicateID,
            x.Name,
            x.Level,
            x.LeaderID,
            x.LeaderName,
            x.MemberCount,
            x.Fund,
            x.ConquerPointsFund,
            x.SyndicateID == holderId,
            x.SyndicateID == winnerId)).ToArray();
        return new RankingPage<GuildRankEntry>(items, total, page, pageSize, DateTime.UtcNow);
    }

    private static async Task<RankingPage<ArenaRankEntry>> QueryArenaRankingAsync(
        ServerDbContext db, string kind, int page, int pageSize)
    {
        var joined =
            from a in db.Arena.AsNoTracking()
            join c in db.Characters.AsNoTracking() on a.CharacterID equals c.CharacterID
            select new
            {
                a.CharacterID,
                c.Name,
                c.Avatar,
                c.Level,
                c.CurrentClass,
                a.Points,
                a.HonorTotal,
                a.WinsToday,
                a.LossesToday,
                a.WinsTotal,
                a.LossesTotal,
            };

        var ordered = kind == "honor"
            ? joined.Where(x => x.HonorTotal > 0).OrderByDescending(x => x.HonorTotal).ThenByDescending(x => x.Points).ThenBy(x => x.CharacterID)
            : joined.OrderByDescending(x => x.Points).ThenByDescending(x => x.WinsTotal).ThenBy(x => x.CharacterID);

        int total = await ordered.CountAsync();
        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        int offset = (page - 1) * pageSize;
        var items = rows.Select((x, idx) => new ArenaRankEntry(
            offset + idx + 1, x.CharacterID, x.Name, x.Avatar, x.Level, x.CurrentClass,
            x.Points, x.HonorTotal, x.WinsToday, x.LossesToday, x.WinsTotal, x.LossesTotal)).ToArray();
        return new RankingPage<ArenaRankEntry>(items, total, page, pageSize, DateTime.UtcNow);
    }

    private static async Task<NobilityBoardCache> BuildNobilityBoardAsync(ServerDbContext db)
    {
        var rows = await (
            from n in db.Nobility.AsNoTracking()
            join c in db.Characters.AsNoTracking() on n.CharacterID equals c.CharacterID
            where n.Donation > 0
            select new { n.CharacterID, n.Donation, c.Name, c.Avatar, c.Level, c.CurrentClass, c.Mesh })
            .ToArrayAsync();

        var ordered = NobilityBoard.Order(rows.Select(r => new Donor(r.CharacterID, r.Name, r.Mesh, r.Donation)));
        var standings = NobilityBoard.Rank(ordered);
        var byId = rows.ToDictionary(r => r.CharacterID);
        var donations = rows.ToDictionary(r => r.CharacterID, r => r.Donation);

        var entries = ordered.Select((donor, idx) =>
        {
            var row = byId[donor.ID];
            var standing = standings[donor.ID];
            return new NobilityRankEntry(
                idx + 1,
                donor.ID,
                donor.Name,
                row.Avatar,
                row.Level,
                row.CurrentClass,
                standing.Rank.ToString(),
                donor.Donation);
        }).ToArray();

        return new NobilityBoardCache(entries, standings, donations, DateTime.UtcNow);
    }

    private sealed record HomeSnapshot(
        GuildWarSummary? GuildWar,
        LevelRankEntry[] TopPlayers,
        NobilityRankEntry[] TopNobility,
        GuildRankEntry[] TopGuilds,
        ArenaRankEntry[] TopArena,
        NewsEntry[] LatestNews);

    internal sealed record NobilityBoardCache(
        NobilityRankEntry[] Entries,
        Dictionary<uint, Standing> Standings,
        Dictionary<uint, ulong> Donations,
        DateTime CachedAt);
}
