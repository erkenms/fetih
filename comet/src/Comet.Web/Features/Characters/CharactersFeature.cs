namespace Comet.Web.Features.Characters;

using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Comet.Game.Database.Models;
using Comet.Game.Items;
using Comet.Game.Nobility;
using Comet.Game.Social;
using Comet.Shared.Models.Admin;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Comet.Web.Features.Catalogs;
using Comet.Web.Features.Items;
using Comet.Web.Features.PublicSite;
using Comet.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using AccountDbContext = Comet.Account.Database.ServerDbContext;
using GameDbContext = Comet.Game.Database.ServerDbContext;

public sealed class CharactersFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config) { }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/characters").RequirePermission(Permission.AdminArea);
        group.MapGet("/{id:int}", GetAsync)
            .WithName("getCharacter")
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:int}/kick", KickAsync)
            .RequirePermission(Permission.Moderate)
            .WithName("kickCharacter")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        group.MapPost("/{id:int}/mute", MuteAsync)
            .RequirePermission(Permission.Moderate)
            .WithName("muteCharacter")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        group.MapPost("/{id:int}/jail", JailAsync)
            .RequirePermission(Permission.Moderate)
            .WithName("jailCharacter")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        group.MapPost("/{id:int}/message", MessageAsync)
            .RequirePermission(Permission.Moderate)
            .WithName("messageCharacter")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:int}/give-item", GiveItemAsync)
            .RequirePermission(Permission.ManageCharacters)
            .WithName("giveCharacterItem")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        group.MapPost("/{id:int}/take-item", TakeItemAsync)
            .RequirePermission(Permission.ManageCharacters)
            .WithName("takeCharacterItem")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        group.MapPost("/{id:int}/silver", AdjustSilverAsync)
            .RequirePermission(Permission.ManageCharacters)
            .WithName("adjustCharacterSilver")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        group.MapPost("/{id:int}/conquer-points", AdjustConquerPointsAsync)
            .RequirePermission(Permission.ManageCharacters)
            .WithName("adjustCharacterConquerPoints")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        group.MapPost("/{id:int}/level", SetLevelAsync)
            .RequirePermission(Permission.ManageCharacters)
            .WithName("setCharacterLevel")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        group.MapPost("/{id:int}/teleport", TeleportAsync)
            .RequirePermission(Permission.ManageCharacters)
            .WithName("teleportCharacter")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<CharacterDetailResponse>, JsonHttpResult<ApiError>>> GetAsync(
        uint id,
        GameRpc rpc,
        ItemCatalog itemCatalog,
        CatalogStore catalogs,
        IMemoryCache cache,
        CancellationToken ct = default)
    {
        await using var gameDb = new GameDbContext();
        var dbChar = await gameDb.Characters.AsNoTracking()
            .SingleOrDefaultAsync(c => c.CharacterID == id, ct);
        if (dbChar == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);

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
                    i.ItemID,
                    i.TypeID,
                    i.Position,
                    i.Amount,
                    i.AmountLimit,
                    stackById.GetValueOrDefault(i.ItemID, (ushort)1),
                    i.Plus,
                    i.Bless,
                    i.Enchant,
                    i.Gem1,
                    i.Gem2,
                    i.Color,
                    i.Locked,
                    i.Package,
                    cat);
            }).ToArray();
        }
        else
        {
            allItems = dbItems.Select(i =>
            {
                itemCatalog.TryGet(i.TypeID, out var cat);
                return new CharacterItemEntry(
                    i.ItemID,
                    i.TypeID,
                    i.Position,
                    i.Amount,
                    i.AmountLimit,
                    i.StackAmount,
                    i.Plus,
                    i.Bless,
                    i.Enchant,
                    i.Gem1,
                    i.Gem2,
                    i.Color,
                    i.Locked,
                    i.Package,
                    cat);
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
                flw.RedRoses, flw.RedRosesToday,
                flw.Lilies, flw.LiliesToday,
                flw.Orchids, flw.OrchidsToday,
                flw.Tulips, flw.TulipsToday)
            : null;

        var arn = await gameDb.Arena.AsNoTracking().SingleOrDefaultAsync(a => a.CharacterID == id, ct);
        var arena = arn != null
            ? new CharacterArenaSummary(
                arn.Points, arn.WinsToday, arn.LossesToday,
                arn.WinsTotal, arn.LossesTotal, arn.HonorPoints, arn.HonorTotal)
            : null;

        uint mapId = live?.MapID ?? dbChar.MapID;

        DateTime? mutedUntil = live != null ? Utc(live.MutedUntil) : Utc(dbChar.MutedUntil);

        return TypedResults.Ok(new CharacterDetailResponse(
            dbChar.CharacterID,
            dbChar.AccountID,
            account?.Username,
            account?.StatusID,
            account?.AuthorityID,
            live?.Name ?? dbChar.Name,
            live?.Spouse ?? dbChar.Spouse,
            live?.Mesh ?? dbChar.Mesh,
            live?.Hairstyle ?? dbChar.Hairstyle,
            live?.Level ?? dbChar.Level,
            live?.Experience ?? dbChar.Experience,
            live?.Class ?? dbChar.CurrentClass,
            live?.PreviousClass ?? dbChar.PreviousClass,
            live?.AncestorClass ?? dbChar.AncestorClass,
            rebirths,
            battlePower,
            live?.Silver ?? dbChar.Silver,
            live?.StoredSilver ?? dbChar.StoredSilver,
            live?.ConquerPoints ?? dbChar.Jewels,
            live?.Strength ?? dbChar.Strength,
            live?.Agility ?? dbChar.Agility,
            live?.Vitality ?? dbChar.Vitality,
            live?.Spirit ?? dbChar.Spirit,
            live?.AttributePoints ?? dbChar.AttributePoints,
            live?.Health ?? dbChar.HealthPoints,
            live?.MaxHealth,
            live?.Mana ?? dbChar.ManaPoints,
            live?.MaxMana,
            live?.PkPoints ?? dbChar.KillPoints,
            live?.Virtue ?? dbChar.Virtue,
            dbChar.VIPLevel,
            mapId,
            catalogs.MapDisplayName(mapId),
            live?.X ?? dbChar.X,
            live?.Y ?? dbChar.Y,
            mutedUntil,
            DateTime.SpecifyKind(dbChar.Registered, DateTimeKind.Utc),
            live?.Online ?? false,
            live?.Dead ?? false,
            live?.IPAddress,
            Utc(live?.LoggedInAt),
            gameConnected,
            equipment,
            inventory,
            warehouses,
            detained,
            magics,
            proficiencies,
            friends,
            enemies,
            tradePartners,
            guild,
            family,
            nobility,
            flowers,
            arena,
            live?.Stats));
    }

    // What the status window's globe shows, less the guild arsenal's share, which only the game
    // server knows: worn gear's quality, sockets, super gems and pluses, doubled for a two-handed
    // weapon, bow or backsword unless a shield is in the left hand. A left-hand weapon adds nothing.
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

    internal static async Task<Results<Ok<CharacterActionResponse>, JsonHttpResult<ApiError>>> KickAsync(
        uint id, KickCharacterRequest request, ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        if (await OutrankedAsync(id, user) is { } outranked)
            return outranked;
        string? reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var result = await rpc.CallAsync<AdminResult>("Kick", new KickArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = id,
            Reason = reason,
        });
        if (!result.Ok)
            return ApiError.FromAdmin(result.Error);

        await audit.WriteAsync("character.kick", "character", id.ToString(CultureInfo.InvariantCulture),
            after: new { reason },
            summary: reason != null ? $"Kicked #{id}: {reason}" : $"Kicked #{id}");
        return TypedResults.Ok(new CharacterActionResponse(true, result.Online));
    }

    internal static async Task<Results<Ok<CharacterActionResponse>, JsonHttpResult<ApiError>>> MuteAsync(
        uint id, MuteCharacterRequest request, ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        if (await OutrankedAsync(id, user) is { } outranked)
            return outranked;
        var result = await rpc.CallAsync<AdminResult>("Mute", new MuteArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = id,
            Minutes = request.Minutes,
        });
        if (!result.Ok)
            return ApiError.FromAdmin(result.Error);

        await audit.WriteAsync("character.mute", "character", id.ToString(CultureInfo.InvariantCulture),
            after: new { minutes = request.Minutes },
            summary: request.Minutes > 0 ? $"Muted #{id} for {request.Minutes} min" : $"Unmuted #{id}");
        return TypedResults.Ok(new CharacterActionResponse(true, result.Online));
    }

    internal static async Task<Results<Ok<CharacterActionResponse>, JsonHttpResult<ApiError>>> JailAsync(
        uint id, ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        if (await OutrankedAsync(id, user) is { } outranked)
            return outranked;
        var result = await rpc.CallAsync<AdminResult>("Jail", new CharacterArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = id,
        });
        if (!result.Ok)
            return ApiError.FromAdmin(result.Error);

        await audit.WriteAsync("character.jail", "character", id.ToString(CultureInfo.InvariantCulture),
            summary: $"Jailed #{id}");
        return TypedResults.Ok(new CharacterActionResponse(true, result.Online));
    }

    internal static async Task<Results<Ok<CharacterActionResponse>, JsonHttpResult<ApiError>>> MessageAsync(
        uint id, MessageCharacterRequest request, ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        if (await OutrankedAsync(id, user) is { } outranked)
            return outranked;
        string text = (request.Text ?? "").Trim();
        if (text.Length == 0 || text.Length > 512)
            return ApiError.Result("invalid_message", StatusCodes.Status400BadRequest);

        var result = await rpc.CallAsync<AdminResult>("Message", new MessageArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = id,
            Text = text,
        });
        if (!result.Ok)
            return ApiError.FromAdmin(result.Error);

        await audit.WriteAsync("character.message", "character", id.ToString(CultureInfo.InvariantCulture),
            after: new { text },
            summary: $"Messaged #{id}: {(text.Length <= 80 ? text : text[..80] + "...")}");
        return TypedResults.Ok(new CharacterActionResponse(true, result.Online));
    }

    internal static async Task<Results<Ok<CharacterActionResponse>, JsonHttpResult<ApiError>>> GiveItemAsync(
        uint id, GiveItemRequest request, ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        if (await OutrankedAsync(id, user) is { } outranked)
            return outranked;
        var result = await rpc.CallAsync<AdminResult>("GiveItem", new GiveItemArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = id,
            TypeID = request.TypeId,
            Count = request.Count,
            Plus = request.Plus,
            Bless = request.Bless,
            Gem1 = request.Gem1,
            Gem2 = request.Gem2,
        });
        if (!result.Ok)
            return ApiError.FromAdmin(result.Error);

        await audit.WriteAsync("character.give_item", "character", id.ToString(CultureInfo.InvariantCulture),
            after: request,
            summary: $"Gave {request.Count}x item {request.TypeId} (+{request.Plus} -{request.Bless}) to #{id}");
        return TypedResults.Ok(new CharacterActionResponse(true, result.Online));
    }

    internal static async Task<Results<Ok<CharacterActionResponse>, JsonHttpResult<ApiError>>> TakeItemAsync(
        uint id, TakeItemRequest request, ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        if (await OutrankedAsync(id, user) is { } outranked)
            return outranked;
        var result = await rpc.CallAsync<AdminResult>("TakeItem", new TakeItemArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = id,
            ItemID = request.ItemId,
        });
        if (!result.Ok)
            return ApiError.FromAdmin(result.Error);

        await audit.WriteAsync("character.take_item", "character", id.ToString(CultureInfo.InvariantCulture),
            before: new { itemId = request.ItemId },
            summary: $"Took item #{request.ItemId} from #{id}");
        return TypedResults.Ok(new CharacterActionResponse(true, result.Online));
    }

    internal static async Task<Results<Ok<CharacterActionResponse>, JsonHttpResult<ApiError>>> AdjustSilverAsync(
        uint id, AdjustAmountRequest request, ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        if (await OutrankedAsync(id, user) is { } outranked)
            return outranked;
        if (request.Amount == 0)
            return ApiError.Result("invalid", StatusCodes.Status400BadRequest);

        var result = await rpc.CallAsync<AdminResult>("AddSilver", new AmountArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = id,
            Amount = request.Amount,
        });
        if (!result.Ok)
            return ApiError.FromAdmin(result.Error);

        await audit.WriteAsync("character.silver", "character", id.ToString(CultureInfo.InvariantCulture),
            after: new { amount = request.Amount },
            summary: $"Silver {request.Amount:+#;-#;0} on #{id}");
        return TypedResults.Ok(new CharacterActionResponse(true, result.Online));
    }

    internal static async Task<Results<Ok<CharacterActionResponse>, JsonHttpResult<ApiError>>> AdjustConquerPointsAsync(
        uint id, AdjustAmountRequest request, ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        if (await OutrankedAsync(id, user) is { } outranked)
            return outranked;
        if (request.Amount == 0)
            return ApiError.Result("invalid", StatusCodes.Status400BadRequest);

        var result = await rpc.CallAsync<AdminResult>("AddConquerPoints", new AmountArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = id,
            Amount = request.Amount,
        });
        if (!result.Ok)
            return ApiError.FromAdmin(result.Error);

        await audit.WriteAsync("character.conquer_points", "character", id.ToString(CultureInfo.InvariantCulture),
            after: new { amount = request.Amount },
            summary: $"CP {request.Amount:+#;-#;0} on #{id}");
        return TypedResults.Ok(new CharacterActionResponse(true, result.Online));
    }

    internal static async Task<Results<Ok<CharacterActionResponse>, JsonHttpResult<ApiError>>> SetLevelAsync(
        uint id, SetLevelRequest request, ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        if (await OutrankedAsync(id, user) is { } outranked)
            return outranked;
        var result = await rpc.CallAsync<AdminResult>("SetLevel", new LevelArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = id,
            Level = request.Level,
        });
        if (!result.Ok)
            return ApiError.FromAdmin(result.Error);

        await audit.WriteAsync("character.set_level", "character", id.ToString(CultureInfo.InvariantCulture),
            after: new { level = request.Level },
            summary: $"Set level of #{id} to {request.Level}");
        return TypedResults.Ok(new CharacterActionResponse(true, result.Online));
    }

    internal static async Task<Results<Ok<CharacterActionResponse>, JsonHttpResult<ApiError>>> TeleportAsync(
        uint id, TeleportCharacterRequest request, ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        if (await OutrankedAsync(id, user) is { } outranked)
            return outranked;
        var result = await rpc.CallAsync<AdminResult>("Teleport", new TeleportArgs
        {
            ActorAccountID = user.AccountId(),
            CharacterID = id,
            MapID = request.MapId,
            X = request.X,
            Y = request.Y,
        });
        if (!result.Ok)
            return ApiError.FromAdmin(result.Error);

        await audit.WriteAsync("character.teleport", "character", id.ToString(CultureInfo.InvariantCulture),
            after: new { mapId = request.MapId, x = request.X, y = request.Y },
            summary: $"Teleported #{id} to map {request.MapId} ({request.X}, {request.Y})");
        return TypedResults.Ok(new CharacterActionResponse(true, result.Online));
    }

    // The account rule of AccountsFeature.OutrankedAsync, for the character's account
    private static async Task<JsonHttpResult<ApiError>?> OutrankedAsync(uint characterId, ClaimsPrincipal user)
    {
        await using var db = new GameDbContext();
        var accountId = await db.Characters.AsNoTracking().Where(c => c.CharacterID == characterId)
            .Select(c => (uint?)c.AccountID).SingleOrDefaultAsync();
        return accountId == null
            ? ApiError.Result("not_found", StatusCodes.Status404NotFound)
            : await Accounts.AccountsFeature.OutrankedAsync(accountId.Value, user);
    }

    private static DateTime? Utc(DateTime? dt) =>
        dt == null ? null : DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc);

}
