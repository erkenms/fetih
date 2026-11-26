namespace Fetih.Web.Features.Players;

using System.Globalization;
using System.Security.Claims;
using Fetih.Account.Database.Models;
using Fetih.Account.Database.Repositories;
using Fetih.Game.Items;
using Fetih.Game.Nobility;
using Fetih.Game.Pk;
using Fetih.Game.Social;
using Fetih.Game.States;
using Fetih.Shared.Models.Admin;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Fetih.Web.Features.Accounts;
using Fetih.Web.Features.Catalogs;
using Fetih.Web.Features.Characters;
using Fetih.Web.Features.Items;
using Fetih.Web.Features.PublicSite;
using Fetih.Web.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using AccountDbContext = Fetih.Account.Database.ServerDbContext;
using GameDbContext = Fetih.Game.Database.ServerDbContext;

public sealed record PlayerQuery(
    string? Q = null,
    string? Ip = null,
    ushort? Status = null,
    ushort? Authority = null,
    bool Online = false,
    bool Jailed = false,
    byte? ClassLine = null,
    uint? MapId = null,
    byte? MinLevel = null,
    byte? MaxLevel = null,
    string? Sort = null);

// Accounts, characters and every moderation/GM action behind /admin/players.
// Errors are short codes (not_found, outranked, invalid_message, …); a GameOfflineException
// propagates from actions so PageChecks flashes "oyun sunucusu kapalı".
public sealed class PlayerService(
    GameRpc rpc,
    CatalogStore catalogs,
    ItemCatalog itemCatalog,
    IMemoryCache cache,
    AuditLog audit)
{
    public const int MaxBulk = 2000;

    public bool GameConnected => rpc.Connected;

    public async Task<PlayerListResponse> ListAsync(
        PlayerQuery f, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var onlineById = new Dictionary<uint, OnlinePlayer>();
        bool gameConnected = rpc.Connected;
        if (gameConnected)
        {
            try
            {
                foreach (var p in await rpc.CallAsync<List<OnlinePlayer>>("OnlinePlayers"))
                    onlineById[p.CharacterID] = p;
            }
            catch (GameOfflineException)
            {
                gameConnected = false;
            }
        }

        if (f.Online && onlineById.Count == 0)
            return new PlayerListResponse([], 0, page, pageSize, gameConnected);

        await using var gameDb = new GameDbContext();
        var charQuery = gameDb.Characters.AsNoTracking();
        if (f.Online)
        {
            var onlineIds = onlineById.Keys.ToArray();
            charQuery = charQuery.Where(c => onlineIds.Contains(c.CharacterID));
        }

        var characters = (await charQuery
            .Select(c => new
            {
                c.CharacterID, c.AccountID, c.Name, c.Level, c.Rebirths, c.CurrentClass,
                c.MapID, c.X, c.Y, c.Silver, c.Jewels, c.KillPoints,
            })
            .ToArrayAsync(ct))
            .Select(c =>
            {
                onlineById.TryGetValue(c.CharacterID, out var live);
                return new
                {
                    c.CharacterID, c.AccountID, c.Name,
                    Level = live?.Level ?? c.Level,
                    Rebirths = live?.Rebirths ?? c.Rebirths,
                    Class = live?.Class ?? c.CurrentClass,
                    MapID = live?.MapID ?? c.MapID,
                    X = live?.X ?? c.X,
                    Y = live?.Y ?? c.Y,
                    c.Silver, c.Jewels,
                    KillPoints = live?.PkPoints ?? c.KillPoints,
                    Online = live != null,
                };
            })
            .Where(c => f.MinLevel == null || c.Level >= f.MinLevel)
            .Where(c => f.MaxLevel == null || c.Level <= f.MaxLevel)
            .Where(c => !f.Jailed || c.MapID == Jail.MapID)
            .Where(c => f.MapId == null || c.MapID == f.MapId)
            .Where(c => f.ClassLine == null || PlayersFeature.ClassLine(c.Class) == f.ClassLine)
            .ToLookup(c => c.AccountID);

        bool needsCharacter = f.Online || f.Jailed || f.ClassLine != null || f.MapId != null
            || f.MinLevel != null || f.MaxLevel != null;

        await using var accountDb = new AccountDbContext();
        var accountQuery = accountDb.Accounts.AsNoTracking();
        if (f.Status.HasValue)
            accountQuery = accountQuery.Where(a => a.StatusID == f.Status.Value);
        if (f.Authority.HasValue)
            accountQuery = accountQuery.Where(a => a.AuthorityID == f.Authority.Value);

        string? ip = string.IsNullOrWhiteSpace(f.Ip) ? null : f.Ip.Trim();
        if (ip != null)
        {
            var loginAccountIds = accountDb.Logins.AsNoTracking()
                .Where(l => l.IPAddress == ip)
                .Select(l => l.AccountID);
            accountQuery = accountQuery.Where(a => a.IPAddress == ip || loginAccountIds.Contains(a.AccountID));
        }

        if (needsCharacter)
        {
            var accountIds = characters.Select(g => g.Key).ToArray();
            accountQuery = accountQuery.Where(a => accountIds.Contains(a.AccountID));
        }

        var accounts = await accountQuery
            .Select(a => new { a.AccountID, a.Username, a.Email, a.Name, a.AuthorityID, a.StatusID, a.IPAddress, a.Registered })
            .ToArrayAsync(ct);

        string? term = string.IsNullOrWhiteSpace(f.Q) ? null : f.Q.Trim();
        bool isId = uint.TryParse(term, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsedId);
        bool Has(string? s) => s != null && s.Contains(term!, StringComparison.OrdinalIgnoreCase);

        var rows = new List<PlayerListItem>();
        foreach (var a in accounts)
        {
            var chars = characters[a.AccountID].ToArray();
            if (chars.Length == 0 && needsCharacter)
                continue;
            bool accountMatch = term == null || (isId && a.AccountID == parsedId)
                || Has(a.Username) || Has(a.Email) || Has(a.Name) || Has(a.IPAddress);
            var registered = DateTime.SpecifyKind(a.Registered, DateTimeKind.Utc);

            if (chars.Length == 0)
            {
                if (accountMatch)
                    rows.Add(new PlayerListItem(a.AccountID, a.Username, a.Email, a.AuthorityID, a.StatusID,
                        a.IPAddress, registered, null, null, null, 0, 0, 0, 0, null, 0, 0, 0, 0, 0, false));
                continue;
            }
            foreach (var c in chars)
            {
                if (!accountMatch && !(isId && c.CharacterID == parsedId) && !Has(c.Name))
                    continue;
                rows.Add(new PlayerListItem(a.AccountID, a.Username, a.Email, a.AuthorityID, a.StatusID,
                    a.IPAddress, registered, null, c.CharacterID, c.Name, c.Level, c.Rebirths, c.Class,
                    c.MapID, catalogs.MapDisplayName(c.MapID), c.X, c.Y, c.Silver, c.Jewels,
                    c.KillPoints, c.Online));
            }
        }

        var sorted = ((f.Sort ?? (f.Online ? "level" : f.Jailed ? "pk" : "newest")) switch
        {
            "level" => rows.OrderByDescending(r => r.Rebirths).ThenByDescending(r => r.Level)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            "name" => rows.OrderBy(r => r.Name ?? r.Username, StringComparer.OrdinalIgnoreCase),
            "silver" => rows.OrderByDescending(r => r.Silver),
            "cp" => rows.OrderByDescending(r => r.ConquerPoints),
            "pk" => rows.OrderByDescending(r => r.KillPoints),
            _ => rows.OrderByDescending(r => r.AccountId),
        }).ToList();

        var items = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var ids = items.Select(r => r.AccountId).ToArray();
        if (ids.Length > 0)
        {
            var lastLogins = await accountDb.Logins.AsNoTracking()
                .Where(l => ids.Contains(l.AccountID))
                .GroupBy(l => l.AccountID)
                .Select(g => new { AccountId = g.Key, Last = g.Max(x => x.Timestamp) })
                .ToDictionaryAsync(x => x.AccountId, x => DateTime.SpecifyKind(x.Last, DateTimeKind.Utc), ct);
            items = items
                .Select(r => lastLogins.TryGetValue(r.AccountId, out var last) ? r with { LastLogin = last } : r)
                .ToArray();
        }

        return new PlayerListResponse(items, sorted.Count, page, pageSize, gameConnected);
    }

    public async Task<uint?> AccountIdForCharacterAsync(uint characterId, CancellationToken ct = default)
    {
        await using var gameDb = new GameDbContext();
        return await gameDb.Characters.AsNoTracking()
            .Where(c => c.CharacterID == characterId)
            .Select(c => (uint?)c.AccountID)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<AccountDetailResponse?> GetAccountAsync(uint id, CancellationToken ct = default)
    {
        await using var accountDb = new AccountDbContext();
        var account = await accountDb.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.AccountID == id, ct);
        if (account == null)
            return null;

        var logins = await accountDb.Logins.AsNoTracking()
            .Where(l => l.AccountID == id)
            .OrderByDescending(l => l.Timestamp)
            .Take(100)
            .Select(l => new AccountLoginEntry(
                DateTime.SpecifyKind(l.Timestamp, DateTimeKind.Utc),
                l.IPAddress))
            .ToArrayAsync(ct);

        var ips = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(account.IPAddress))
            ips.Add(account.IPAddress);
        foreach (var login in logins)
        {
            if (!string.IsNullOrWhiteSpace(login.IpAddress))
                ips.Add(login.IpAddress);
        }

        SameIpAccountSummary[] sameIp = [];
        if (ips.Count > 0)
        {
            var ipList = ips.ToArray();
            var loginHits = await accountDb.Logins.AsNoTracking()
                .Where(l => l.AccountID != id && ipList.Contains(l.IPAddress))
                .Select(l => new { l.AccountID, l.IPAddress })
                .Distinct()
                .ToArrayAsync(ct);

            var directMatches = await accountDb.Accounts.AsNoTracking()
                .Where(a => a.AccountID != id && a.IPAddress != null && ipList.Contains(a.IPAddress))
                .Select(a => new { a.AccountID, a.IPAddress })
                .ToArrayAsync(ct);

            var sharedByAccount = new Dictionary<uint, HashSet<string>>();
            foreach (var hit in loginHits)
            {
                if (!sharedByAccount.TryGetValue(hit.AccountID, out var set))
                    sharedByAccount[hit.AccountID] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(hit.IPAddress);
            }
            foreach (var hit in directMatches)
            {
                if (string.IsNullOrWhiteSpace(hit.IPAddress)) continue;
                if (!sharedByAccount.TryGetValue(hit.AccountID, out var set))
                    sharedByAccount[hit.AccountID] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(hit.IPAddress);
            }

            if (sharedByAccount.Count > 0)
            {
                var otherIds = sharedByAccount.Keys.Take(50).ToArray();
                var others = await accountDb.Accounts.AsNoTracking()
                    .Where(a => otherIds.Contains(a.AccountID))
                    .OrderByDescending(a => a.AccountID)
                    .ToArrayAsync(ct);
                sameIp = others.Select(o => new SameIpAccountSummary(
                    o.AccountID, o.Username, o.AuthorityID, o.StatusID,
                    DateTime.SpecifyKind(o.Registered, DateTimeKind.Utc),
                    sharedByAccount[o.AccountID].Order(StringComparer.Ordinal).ToArray())).ToArray();
            }
        }

        var onlineCharIds = new HashSet<uint>();
        bool gameConnected = rpc.Connected;
        bool packetTraced = false;
        if (gameConnected)
        {
            try
            {
                var online = await rpc.CallAsync<List<OnlinePlayer>>("OnlinePlayers");
                foreach (var p in online)
                    onlineCharIds.Add(p.CharacterID);
                packetTraced = await rpc.CallAsync<bool>("IsPacketTraced", id);
            }
            catch (GameOfflineException)
            {
                gameConnected = false;
            }
        }

        await using var gameDb = new GameDbContext();
        var rawChars = await gameDb.Characters.AsNoTracking()
            .Where(c => c.AccountID == id)
            .OrderByDescending(c => c.Level)
            .ThenBy(c => c.Name)
            .ToArrayAsync(ct);

        var characters = rawChars.Select(c => new AccountCharacterSummary(
            c.CharacterID, c.Name, c.Level, c.Rebirths, c.CurrentClass,
            c.MapID, catalogs.MapDisplayName(c.MapID), c.Silver, c.Jewels,
            onlineCharIds.Contains(c.CharacterID))).ToArray();

        return new AccountDetailResponse(
            account.AccountID, account.Username, account.Email, account.Name,
            account.AuthorityID, account.StatusID, account.IPAddress,
            DateTime.SpecifyKind(account.Registered, DateTimeKind.Utc),
            gameConnected, packetTraced, characters, logins, sameIp);
    }

    public async Task<CharacterDetailResponse?> GetCharacterAsync(uint id, CancellationToken ct = default)
    {
        await using var gameDb = new GameDbContext();
        var dbChar = await gameDb.Characters.AsNoTracking()
            .SingleOrDefaultAsync(c => c.CharacterID == id, ct);
        if (dbChar == null)
            return null;

        CharacterState? live = null;
        bool gameConnected = rpc.Connected;
        if (gameConnected)
        {
            try
            {
                live = await rpc.CallAsync<CharacterState?>("Character", id);
            }
            catch (GameOfflineException)
            {
                gameConnected = false;
            }
        }

        await using var accountDb = new AccountDbContext();
        var account = await accountDb.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.AccountID == dbChar.AccountID, ct);

        var dbItems = await gameDb.Items.AsNoTracking()
            .Where(i => i.CharacterID == id)
            .OrderBy(i => i.Position)
            .ThenBy(i => i.ItemID)
            .ToArrayAsync(ct);

        CharacterItemEntry[] allItems;
        if (live is { Online: true, Items: not null })
        {
            var stackById = dbItems.ToDictionary(i => i.ItemID, i => i.StackAmount);
            allItems = live.Items.Select(i =>
            {
                itemCatalog.TryGet(i.TypeID, out var cat);
                return new CharacterItemEntry(
                    i.ItemID, i.TypeID, i.Position, i.Amount, i.AmountLimit,
                    stackById.GetValueOrDefault(i.ItemID, (ushort)1),
                    i.Plus, i.Bless, i.Enchant, i.Gem1, i.Gem2, i.Color, i.Locked, i.Package, cat);
            }).ToArray();
        }
        else
        {
            allItems = dbItems.Select(i =>
            {
                itemCatalog.TryGet(i.TypeID, out var cat);
                return new CharacterItemEntry(
                    i.ItemID, i.TypeID, i.Position, i.Amount, i.AmountLimit, i.StackAmount,
                    i.Plus, i.Bless, i.Enchant, i.Gem1, i.Gem2, i.Color, i.Locked, i.Package, cat);
            }).ToArray();
        }

        var equipment = allItems
            .Where(i => i.Position is >= (byte)ItemPosition.Headwear and <= (byte)ItemPosition.Crop)
            .OrderBy(i => i.Position)
            .ToArray();
        var inventory = allItems
            .Where(i => i.Position == (byte)ItemPosition.Inventory)
            .OrderBy(i => i.ItemId)
            .ToArray();
        var warehouses = allItems
            .Where(i => i.Position == (byte)ItemPosition.Warehouse)
            .GroupBy(i => i.Package)
            .OrderBy(g => g.Key)
            .Select(g => new CharacterWarehouseGroup(g.Key, g.OrderBy(x => x.ItemId).ToArray()))
            .ToArray();
        var detained = allItems
            .Where(i => i.Position == (byte)ItemPosition.Detained)
            .OrderBy(i => i.ItemId)
            .ToArray();

        var rawMagics = await gameDb.Magics.AsNoTracking()
            .Where(m => m.CharacterID == id)
            .OrderBy(m => m.Type)
            .ToArrayAsync(ct);
        var magics = rawMagics.Select(m =>
        {
            catalogs.SpellsByType.TryGetValue(m.Type, out var spell);
            return new CharacterMagicEntry(m.Type, spell?.Name, m.Level, m.Experience, m.OldLevel);
        }).ToArray();

        var proficiencies = await gameDb.Proficiencies.AsNoTracking()
            .Where(p => p.CharacterID == id)
            .OrderBy(p => p.Type)
            .Select(p => new CharacterProficiencyEntry(p.Type, p.Level, p.Experience))
            .ToArrayAsync(ct);

        var friendIds = await gameDb.Friends.AsNoTracking()
            .Where(f => f.CharacterID == id)
            .Select(f => f.FriendID)
            .ToArrayAsync(ct);
        var enemyRows = await gameDb.Enemies.AsNoTracking()
            .Where(e => e.CharacterID == id)
            .OrderByDescending(e => e.AddedAt)
            .ToArrayAsync(ct);
        var partnerRows = await gameDb.TradePartners.AsNoTracking()
            .Where(t => t.CharacterID == id)
            .OrderBy(t => t.Since)
            .ToArrayAsync(ct);

        var relatedIds = friendIds
            .Concat(enemyRows.Select(e => e.EnemyID))
            .Concat(partnerRows.Select(p => p.PartnerID))
            .Distinct()
            .ToArray();
        var relatedChars = relatedIds.Length == 0
            ? new Dictionary<uint, (string Name, byte Level, byte Class)>()
            : (await gameDb.Characters.AsNoTracking()
                .Where(c => relatedIds.Contains(c.CharacterID))
                .Select(c => new { c.CharacterID, c.Name, c.Level, c.CurrentClass })
                .ToArrayAsync(ct))
                .ToDictionary(c => c.CharacterID, c => (c.Name, c.Level, Class: c.CurrentClass));

        var friends = friendIds
            .Select(fid => relatedChars.TryGetValue(fid, out var info)
                ? new CharacterRelationEntry(fid, info.Name, info.Level, info.Class, null)
                : new CharacterRelationEntry(fid, $"#{fid}", 0, 0, null))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var enemies = enemyRows
            .Select(e => relatedChars.TryGetValue(e.EnemyID, out var info)
                ? new CharacterRelationEntry(e.EnemyID, info.Name, info.Level, info.Class, DateTime.SpecifyKind(e.AddedAt, DateTimeKind.Utc))
                : new CharacterRelationEntry(e.EnemyID, $"#{e.EnemyID}", 0, 0, DateTime.SpecifyKind(e.AddedAt, DateTimeKind.Utc)))
            .ToArray();
        var tradePartners = partnerRows
            .Select(p => relatedChars.TryGetValue(p.PartnerID, out var info)
                ? new CharacterRelationEntry(p.PartnerID, info.Name, info.Level, info.Class, DateTime.SpecifyKind(p.Since, DateTimeKind.Utc))
                : new CharacterRelationEntry(p.PartnerID, $"#{p.PartnerID}", 0, 0, DateTime.SpecifyKind(p.Since, DateTimeKind.Utc)))
            .ToArray();

        CharacterGuildSummary? guild = null;
        var synMember = await gameDb.SyndicateMembers.AsNoTracking()
            .SingleOrDefaultAsync(m => m.CharacterID == id, ct);
        if (synMember != null)
        {
            var syn = await gameDb.Syndicates.AsNoTracking()
                .SingleOrDefaultAsync(s => s.SyndicateID == synMember.SyndicateID, ct);
            if (syn != null)
                guild = new CharacterGuildSummary(
                    syn.SyndicateID, syn.Name, synMember.Rank,
                    synMember.SilverDonation, synMember.ConquerPointsDonation, synMember.Joined);
        }

        CharacterFamilySummary? family = null;
        var famMember = await gameDb.FamilyMembers.AsNoTracking()
            .SingleOrDefaultAsync(m => m.CharacterID == id, ct);
        if (famMember != null)
        {
            var fam = await gameDb.Families.AsNoTracking()
                .SingleOrDefaultAsync(f => f.FamilyID == famMember.FamilyID, ct);
            if (fam != null)
                family = new CharacterFamilySummary(
                    fam.FamilyID, fam.Name, famMember.Rank, famMember.Donation, famMember.Joined);
        }

        var nob = await gameDb.Nobility.AsNoTracking().SingleOrDefaultAsync(n => n.CharacterID == id, ct);
        var nobility = nob != null ? new CharacterNobilitySummary(nob.Donation) : null;
        var nobilityBoard = await PublicSiteFeature.GetCachedNobilityBoardAsync(cache);
        var nobilityRank = nobilityBoard.Standings.TryGetValue(id, out var standing) ? standing.Rank : NobilityRank.Commoner;
        byte level = live?.Level ?? dbChar.Level;
        byte rebirths = live?.Rebirths ?? dbChar.Rebirths;
        uint battlePower = level + rebirths * 5u + (uint)nobilityRank + GearBattlePower(equipment);

        var flw = await gameDb.Flowers.AsNoTracking().SingleOrDefaultAsync(f => f.CharacterID == id, ct);
        var flowers = flw != null
            ? new CharacterFlowerSummary(
                flw.RedRoses, flw.RedRosesToday, flw.Lilies, flw.LiliesToday,
                flw.Orchids, flw.OrchidsToday, flw.Tulips, flw.TulipsToday)
            : null;

        var arn = await gameDb.Arena.AsNoTracking().SingleOrDefaultAsync(a => a.CharacterID == id, ct);
        var arena = arn != null
            ? new CharacterArenaSummary(
                arn.Points, arn.WinsToday, arn.LossesToday,
                arn.WinsTotal, arn.LossesTotal, arn.HonorPoints, arn.HonorTotal)
            : null;

        uint mapId = live?.MapID ?? dbChar.MapID;
        DateTime? mutedUntil = live != null ? Utc(live.MutedUntil) : Utc(dbChar.MutedUntil);

        return new CharacterDetailResponse(
            dbChar.CharacterID, dbChar.AccountID, account?.Username, account?.StatusID, account?.AuthorityID,
            live?.Name ?? dbChar.Name, live?.Spouse ?? dbChar.Spouse, live?.Mesh ?? dbChar.Mesh,
            live?.Hairstyle ?? dbChar.Hairstyle, live?.Level ?? dbChar.Level, live?.Experience ?? dbChar.Experience,
            live?.Class ?? dbChar.CurrentClass, live?.PreviousClass ?? dbChar.PreviousClass,
            live?.AncestorClass ?? dbChar.AncestorClass, rebirths, battlePower,
            live?.Silver ?? dbChar.Silver, live?.StoredSilver ?? dbChar.StoredSilver,
            live?.ConquerPoints ?? dbChar.Jewels,
            live?.Strength ?? dbChar.Strength, live?.Agility ?? dbChar.Agility,
            live?.Vitality ?? dbChar.Vitality, live?.Spirit ?? dbChar.Spirit,
            live?.AttributePoints ?? dbChar.AttributePoints,
            live?.Health ?? dbChar.HealthPoints, live?.MaxHealth,
            live?.Mana ?? dbChar.ManaPoints, live?.MaxMana,
            live?.PkPoints ?? dbChar.KillPoints, live?.Virtue ?? dbChar.Virtue, dbChar.VIPLevel,
            mapId, catalogs.MapDisplayName(mapId), live?.X ?? dbChar.X, live?.Y ?? dbChar.Y,
            mutedUntil, DateTime.SpecifyKind(dbChar.Registered, DateTimeKind.Utc),
            live?.Online ?? false, live?.Dead ?? false, live?.IPAddress, Utc(live?.LoggedInAt),
            gameConnected, equipment, inventory, warehouses, detained, magics, proficiencies,
            friends, enemies, tradePartners, guild, family, nobility, flowers, arena, live?.Stats);
    }

    // Account actions

    public async Task<string?> BanAsync(uint accountId, string? reason, ClaimsPrincipal user)
    {
        if (accountId == user.AccountId())
            return "self_ban";
        if (await CheckAccountRankAsync(accountId, user) is { } err)
            return err;

        reason = TrimOrNull(reason);
        var result = await rpc.CallAsync<AdminResult>("Ban", new BanArgs
        {
            ActorAccountID = user.AccountId(),
            AccountID = accountId,
            Reason = reason,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("account.ban", "account", accountId.ToString(CultureInfo.InvariantCulture),
            after: new { statusId = (ushort)AccountStatus.Banned, reason },
            summary: reason != null ? $"Banned account #{accountId}: {reason}" : $"Banned account #{accountId}");
        return null;
    }

    public async Task<string?> UnbanAsync(uint accountId, string? reason, ClaimsPrincipal user)
    {
        if (await CheckAccountRankAsync(accountId, user) is { } err)
            return err;

        reason = TrimOrNull(reason);
        var result = await rpc.CallAsync<AdminResult>("Unban", new BanArgs
        {
            ActorAccountID = user.AccountId(),
            AccountID = accountId,
            Reason = reason,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("account.unban", "account", accountId.ToString(CultureInfo.InvariantCulture),
            after: new { statusId = (ushort)AccountStatus.Activated, reason },
            summary: reason != null ? $"Unbanned account #{accountId}: {reason}" : $"Unbanned account #{accountId}");
        return null;
    }

    public async Task<string?> SetStatusAsync(
        uint accountId, ushort statusId, string? reason, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (statusId == (ushort)AccountStatus.Banned)
            return "use_ban_endpoint";
        if (!Enum.IsDefined(typeof(AccountStatus), statusId))
            return "invalid_status";
        if (accountId == user.AccountId() && statusId == (ushort)AccountStatus.Locked)
            return "self_lock";

        await using var db = new AccountDbContext();
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.AccountID == accountId, ct);
        if (account == null)
            return "not_found";

        ushort previous = account.StatusID;
        account.StatusID = statusId;
        await db.SaveChangesAsync(ct);

        reason = TrimOrNull(reason);
        await audit.WriteAsync("account.set_status", "account", accountId.ToString(CultureInfo.InvariantCulture),
            before: new { statusId = previous },
            after: new { statusId, reason },
            summary: $"Status {(AccountStatus)previous} -> {(AccountStatus)statusId}");
        return null;
    }

    public async Task<string?> SetAuthorityAsync(
        uint accountId, ushort authorityId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(typeof(Authority), authorityId))
            return "invalid_authority";
        if (accountId == user.AccountId() && authorityId < (ushort)Authority.Administrator)
            return "self_demotion";

        await using var db = new AccountDbContext();
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.AccountID == accountId, ct);
        if (account == null)
            return "not_found";

        ushort previous = account.AuthorityID;
        account.AuthorityID = authorityId;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync("account.set_authority", "account", accountId.ToString(CultureInfo.InvariantCulture),
            before: new { authorityId = previous },
            after: new { authorityId },
            summary: $"Authority {(Authority)previous} -> {(Authority)authorityId}");
        return null;
    }

    public async Task<string?> ResetPasswordAsync(uint accountId, string? newPassword, CancellationToken ct = default)
    {
        string password = newPassword ?? "";
        if (AccountRules.CheckPassword(password) is string validationError)
            return validationError;

        await using var db = new AccountDbContext();
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.AccountID == accountId, ct);
        if (account == null)
            return "not_found";

        string? error = await AccountsRepository.SetPasswordAsync(account.Username, password);
        if (error != null)
            return error;

        await audit.WriteAsync("account.reset_password", "account", accountId.ToString(CultureInfo.InvariantCulture),
            summary: $"Password reset for {account.Username}");
        return null;
    }

    public async Task<string?> SetPacketTraceAsync(
        uint accountId, bool enabled, ClaimsPrincipal user, CancellationToken ct = default)
    {
        await using var db = new AccountDbContext();
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.AccountID == accountId, ct);
        if (account == null)
            return "not_found";

        var result = await rpc.CallAsync<AdminResult>("SetPacketTrace", new PacketTraceArgs
        {
            ActorAccountID = user.AccountId(),
            AccountID = accountId,
            Enabled = enabled,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("account.packet_trace", "account", accountId.ToString(CultureInfo.InvariantCulture),
            after: new { enabled },
            summary: $"{(enabled ? "Enabled" : "Disabled")} packet trace for {account.Username}");
        return null;
    }

    // Character actions

    public async Task<string?> KickAsync(uint characterId, string? reason, ClaimsPrincipal user)
    {
        if (await CheckCharacterRankAsync(characterId, user) is { } err)
            return err;

        reason = TrimOrNull(reason);
        var result = await rpc.CallAsync<AdminResult>("Kick", new KickArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = characterId,
            Reason = reason,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("character.kick", "character", characterId.ToString(CultureInfo.InvariantCulture),
            after: new { reason },
            summary: reason != null ? $"Kicked #{characterId}: {reason}" : $"Kicked #{characterId}");
        return null;
    }

    public async Task<string?> MuteAsync(uint characterId, int minutes, ClaimsPrincipal user)
    {
        if (await CheckCharacterRankAsync(characterId, user) is { } err)
            return err;

        var result = await rpc.CallAsync<AdminResult>("Mute", new MuteArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = characterId,
            Minutes = minutes,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("character.mute", "character", characterId.ToString(CultureInfo.InvariantCulture),
            after: new { minutes },
            summary: minutes > 0 ? $"Muted #{characterId} for {minutes} min" : $"Unmuted #{characterId}");
        return null;
    }

    public async Task<string?> JailAsync(uint characterId, ClaimsPrincipal user)
    {
        if (await CheckCharacterRankAsync(characterId, user) is { } err)
            return err;

        var result = await rpc.CallAsync<AdminResult>("Jail", new CharacterArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = characterId,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("character.jail", "character", characterId.ToString(CultureInfo.InvariantCulture),
            summary: $"Jailed #{characterId}");
        return null;
    }

    public async Task<string?> ReleaseFromJailAsync(uint characterId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (await CheckCharacterRankAsync(characterId, user) is { } err)
            return err;

        await using var db = new GameDbContext();
        var before = await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.CharacterID == characterId, ct);
        if (before == null)
            return "not_found";

        var result = await rpc.CallAsync<AdminResult>("ReleaseFromJail", new CharacterArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = characterId,
        });
        if (!result.Ok)
            return result.Error == AdminErrors.Invalid ? "not_in_jail" : (result.Error ?? "failed");

        await audit.WriteAsync("pk.release", "character", characterId.ToString(CultureInfo.InvariantCulture),
            before: new { before.Name, before.MapID, before.X, before.Y, before.KillPoints },
            summary: $"Released {before.Name} from jail");
        return null;
    }

    public async Task<string?> MessageAsync(uint characterId, string? text, ClaimsPrincipal user)
    {
        if (await CheckCharacterRankAsync(characterId, user) is { } err)
            return err;

        string trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length > 512)
            return "invalid_message";

        var result = await rpc.CallAsync<AdminResult>("Message", new MessageArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = characterId,
            Text = trimmed,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("character.message", "character", characterId.ToString(CultureInfo.InvariantCulture),
            after: new { text = trimmed },
            summary: $"Messaged #{characterId}: {(trimmed.Length <= 80 ? trimmed : trimmed[..80] + "...")}");
        return null;
    }

    public async Task<string?> GiveItemAsync(uint characterId, GiveItemRequest request, ClaimsPrincipal user)
    {
        if (await CheckCharacterRankAsync(characterId, user) is { } err)
            return err;
        if (request.TypeId == 0)
            return "unknown_item_type";

        var result = await rpc.CallAsync<AdminResult>("GiveItem", new GiveItemArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = characterId,
            TypeID = request.TypeId,
            Count = request.Count,
            Plus = request.Plus,
            Bless = request.Bless,
            Gem1 = request.Gem1,
            Gem2 = request.Gem2,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("character.give_item", "character", characterId.ToString(CultureInfo.InvariantCulture),
            after: request,
            summary: $"Gave {request.Count}x item {request.TypeId} (+{request.Plus} -{request.Bless}) to #{characterId}");
        return null;
    }

    public async Task<string?> TakeItemAsync(uint characterId, uint itemId, ClaimsPrincipal user)
    {
        if (await CheckCharacterRankAsync(characterId, user) is { } err)
            return err;

        var result = await rpc.CallAsync<AdminResult>("TakeItem", new TakeItemArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = characterId,
            ItemID = itemId,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("character.take_item", "character", characterId.ToString(CultureInfo.InvariantCulture),
            before: new { itemId },
            summary: $"Took item #{itemId} from #{characterId}");
        return null;
    }

    public async Task<string?> AdjustSilverAsync(uint characterId, long amount, ClaimsPrincipal user)
    {
        if (await CheckCharacterRankAsync(characterId, user) is { } err)
            return err;
        if (amount == 0)
            return "invalid";

        var result = await rpc.CallAsync<AdminResult>("AddSilver", new AmountArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = characterId,
            Amount = amount,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("character.silver", "character", characterId.ToString(CultureInfo.InvariantCulture),
            after: new { amount },
            summary: $"Silver {amount:+#;-#;0} on #{characterId}");
        return null;
    }

    public async Task<string?> AdjustConquerPointsAsync(uint characterId, long amount, ClaimsPrincipal user)
    {
        if (await CheckCharacterRankAsync(characterId, user) is { } err)
            return err;
        if (amount == 0)
            return "invalid";

        var result = await rpc.CallAsync<AdminResult>("AddConquerPoints", new AmountArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = characterId,
            Amount = amount,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("character.conquer_points", "character", characterId.ToString(CultureInfo.InvariantCulture),
            after: new { amount },
            summary: $"CP {amount:+#;-#;0} on #{characterId}");
        return null;
    }

    public async Task<string?> SetLevelAsync(uint characterId, byte level, ClaimsPrincipal user)
    {
        if (await CheckCharacterRankAsync(characterId, user) is { } err)
            return err;

        var result = await rpc.CallAsync<AdminResult>("SetLevel", new LevelArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = characterId,
            Level = level,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("character.set_level", "character", characterId.ToString(CultureInfo.InvariantCulture),
            after: new { level },
            summary: $"Set level of #{characterId} to {level}");
        return null;
    }

    public async Task<string?> TeleportAsync(uint characterId, uint mapId, ushort x, ushort y, ClaimsPrincipal user)
    {
        if (await CheckCharacterRankAsync(characterId, user) is { } err)
            return err;

        var result = await rpc.CallAsync<AdminResult>("Teleport", new TeleportArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = characterId,
            MapID = mapId,
            X = x,
            Y = y,
        });
        if (!result.Ok)
            return result.Error ?? "failed";

        await audit.WriteAsync("character.teleport", "character", characterId.ToString(CultureInfo.InvariantCulture),
            after: new { mapId, x, y },
            summary: $"Teleported #{characterId} to map {mapId} ({x}, {y})");
        return null;
    }

    // Bulk actions on selected accounts

    public async Task<PlayerBulkResponse> BulkAsync(
        string action, uint[] accountIds, string? reason, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var unique = accountIds.Distinct().Take(MaxBulk).ToArray();
        await using var gameDb = new GameDbContext();
        var charByAccount = (await gameDb.Characters.AsNoTracking()
                .Where(c => unique.Contains(c.AccountID))
                .Select(c => new { c.AccountID, c.CharacterID, c.Name })
                .ToArrayAsync(ct))
            .GroupBy(c => c.AccountID)
            .ToDictionary(g => g.Key, g => g.First());

        int succeeded = 0;
        var failures = new List<PlayerBulkFailure>();
        foreach (uint accountId in unique)
        {
            charByAccount.TryGetValue(accountId, out var ch);
            string? error;
            try
            {
                error = action switch
                {
                    "ban" => await this.BanAsync(accountId, reason, user),
                    "unban" => await this.UnbanAsync(accountId, reason, user),
                    "kick" => ch == null ? "no_character" : await this.KickAsync(ch.CharacterID, reason, user),
                    _ => "invalid_action",
                };
            }
            catch (GameOfflineException)
            {
                error = "game_offline";
            }

            if (error == null)
                succeeded++;
            else
                failures.Add(new PlayerBulkFailure(accountId, ch?.Name, error));
        }

        return new PlayerBulkResponse(succeeded, failures.ToArray());
    }

    private static async Task<string?> CheckAccountRankAsync(uint accountId, ClaimsPrincipal user)
    {
        await using var db = new AccountDbContext();
        var authority = await db.Accounts.AsNoTracking().Where(a => a.AccountID == accountId)
            .Select(a => (ushort?)a.AuthorityID).SingleOrDefaultAsync();
        if (authority == null)
            return "not_found";
        return user.Outranks(authority.Value) ? null : "outranked";
    }

    private static async Task<string?> CheckCharacterRankAsync(uint characterId, ClaimsPrincipal user)
    {
        await using var db = new GameDbContext();
        var accountId = await db.Characters.AsNoTracking().Where(c => c.CharacterID == characterId)
            .Select(c => (uint?)c.AccountID).SingleOrDefaultAsync();
        return accountId == null ? "not_found" : await CheckAccountRankAsync(accountId.Value, user);
    }

    private static uint GearBattlePower(CharacterItemEntry[] equipment)
    {
        bool shield = equipment.Any(i => i.Position == (byte)ItemPosition.LeftHand && ItemSlots.IsShield(i.TypeId));
        uint total = 0;
        foreach (var item in equipment)
        {
            if (item.Position is < (byte)ItemPosition.Headwear or > (byte)ItemPosition.Boots)
                continue;
            if (item.Position == (byte)ItemPosition.LeftHand && !ItemSlots.IsShield(item.TypeId))
                continue;
            uint bp = ArsenalConfig.ItemBattlePower(item.TypeId, item.Plus, item.Gem1, item.Gem2);
            if (item.Position == (byte)ItemPosition.RightHand && !shield
                && (ItemSlots.IsTwoHanded(item.TypeId) || item.TypeId / 1000 == 421))
                bp *= 2;
            total += bp;
        }
        return total;
    }

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateTime? Utc(DateTime? dt) =>
        dt == null ? null : DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc);
}
