namespace Comet.Core.Logging
{
    using System;
    using Serilog;

    public static class LogEvents
    {
        public const string ServerStart = "server.start";
        public const string ServerStop = "server.stop";

        public const string NetConnect = "net.connect";
        public const string NetDisconnect = "net.disconnect";
        public const string NetRejected = "net.rejected";
        public const string RpcConnected = "rpc.connected";

        public const string AccountLogin = "account.login";
        public const string AccountLoginFailed = "account.login_failed";
        public const string AccountCreated = "account.created";
        public const string AccountCreateFailed = "account.create_failed";

        public const string GameAuthFailed = "game.auth_failed";
        public const string SessionLogin = "session.login";
        public const string SessionLogout = "session.logout";
        public const string SessionReplaced = "session.replaced";
        public const string CharacterCreated = "character.created";
        public const string CharacterCreateFailed = "character.create_failed";
        public const string CharacterRelocated = "character.relocated";
        public const string CharacterSaved = "character.saved";
        public const string CharacterSaveFailed = "character.save_failed";
        public const string CharacterSaveRound = "character.save_round";
        public const string CharacterLevel = "character.level";
        public const string CharacterAllotted = "character.allotted";
        public const string CharacterAllotRejected = "character.allot_rejected";
        public const string CharacterSilver = "character.silver";
        public const string CharacterConquerPoints = "character.conquer_points";
        public const string CharacterStoredSilver = "character.stored_silver";
        public const string CharacterDied = "character.died";
        public const string CharacterRevived = "character.revived";
        public const string CharacterReviveRejected = "character.revive_rejected";
        public const string CharacterBlessed = "character.blessed";
        public const string CharacterBlessingEnded = "character.blessing_ended";
        public const string TrainingExpClaimed = "training.exp_claimed";
        public const string TrainingOfflineStarted = "training.offline_started";
        public const string TrainingOfflineClaimed = "training.offline_claimed";
        public const string TrainingRejected = "training.rejected";

        public const string ItemTypesLoaded = "item.types_loaded";
        public const string ItemCreated = "item.created";
        public const string ItemEquipped = "item.equipped";
        public const string ItemUnequipped = "item.unequipped";
        public const string ItemEquipRejected = "item.equip_rejected";
        public const string ItemDropped = "item.dropped";
        public const string ItemPickedUp = "item.picked_up";
        public const string ItemExpired = "item.expired";
        public const string ItemUsed = "item.used";
        public const string ItemUseRejected = "item.use_rejected";
        public const string ItemGroundRejected = "item.ground_rejected";
        public const string ItemBought = "item.bought";
        public const string ItemSold = "item.sold";
        public const string ItemShopRejected = "item.shop_rejected";
        public const string ItemStored = "item.stored";
        public const string ItemTakenOut = "item.taken_out";
        public const string ItemWarehouseRejected = "item.warehouse_rejected";
        public const string ShopsLoaded = "item.shops_loaded";
        public const string ItemLocked = "item.locked";
        public const string ItemUnlockStarted = "item.unlock_started";
        public const string ItemUnlocked = "item.unlocked";
        public const string ItemLockRejected = "item.lock_rejected";

        public const string SecondPasswordSet = "second_password.set";
        public const string SecondPasswordVerified = "second_password.verified";
        public const string SecondPasswordRejected = "second_password.rejected";

        public const string TradeRequested = "trade.requested";
        public const string TradeOpened = "trade.opened";
        public const string TradeOffered = "trade.offered";
        public const string TradeCompleted = "trade.completed";
        public const string TradeCancelled = "trade.cancelled";
        public const string TradeRejected = "trade.rejected";

        public const string BoothOpened = "booth.opened";
        public const string BoothClosed = "booth.closed";
        public const string BoothListed = "booth.listed";
        public const string BoothUnlisted = "booth.unlisted";
        public const string BoothSold = "booth.sold";
        public const string BoothRejected = "booth.rejected";

        public const string TeamCreated = "team.created";
        public const string TeamJoined = "team.joined";
        public const string TeamLeft = "team.left";
        public const string TeamDismissed = "team.dismissed";
        public const string TeamRejected = "team.rejected";

        public const string FriendAdded = "friend.added";
        public const string FriendRemoved = "friend.removed";
        public const string FriendRejected = "friend.rejected";
        public const string EnemyRemoved = "enemy.removed";

        public const string TradePartnerRequested = "trade_partner.requested";
        public const string TradePartnerAdded = "trade_partner.added";
        public const string TradePartnerRemoved = "trade_partner.removed";
        public const string TradePartnerRejected = "trade_partner.rejected";

        public const string GuildsLoaded = "guild.loaded";
        public const string GuildCreated = "guild.created";
        public const string GuildDisbanded = "guild.disbanded";
        public const string GuildJoined = "guild.joined";
        public const string GuildLeft = "guild.left";
        public const string GuildRank = "guild.rank";
        public const string GuildDonated = "guild.donated";
        public const string GuildRelation = "guild.relation";
        public const string GuildAnnounced = "guild.announced";
        public const string GuildRejected = "guild.rejected";

        public const string ClansLoaded = "clan.loaded";
        public const string ClanCreated = "clan.created";
        public const string ClanDisbanded = "clan.disbanded";
        public const string ClanJoined = "clan.joined";
        public const string ClanLeft = "clan.left";
        public const string ClanLeader = "clan.leader";
        public const string ClanDonated = "clan.donated";
        public const string ClanRelation = "clan.relation";
        public const string ClanAnnounced = "clan.announced";
        public const string ClanRejected = "clan.rejected";

        public const string PkMode = "pk.mode";
        public const string PkModeRejected = "pk.mode_rejected";
        public const string PkPoints = "pk.points";
        public const string PkFlashing = "pk.flashing";
        public const string PkKill = "pk.kill";
        public const string PkJailed = "pk.jailed";
        public const string PkReleased = "pk.released";

        public const string MapIndexLoaded = "map.index_loaded";
        public const string MapLoaded = "map.loaded";
        public const string MapLoadFailed = "map.load_failed";
        public const string PortalsLoaded = "map.portals_loaded";
        public const string MapTypesLoaded = "map.types_loaded";

        public const string NpcsLoaded = "npc.loaded";
        public const string NpcTalk = "npc.talk";
        public const string NpcRejected = "npc.rejected";
        public const string TeleportersLoaded = "npc.teleporters_loaded";

        public const string MonstersLoaded = "monster.loaded";
        public const string MonsterKilled = "monster.killed";
        public const string MonsterRespawned = "monster.respawned";
        public const string MonsterDrop = "monster.drop";
        public const string MonsterLoopFailed = "monster.loop_failed";

        public const string CombatHit = "combat.hit";
        public const string CombatRejected = "combat.rejected";

        public const string MagicTypesLoaded = "magic.types_loaded";
        public const string MagicCast = "magic.cast";
        public const string MagicRejected = "magic.rejected";
        public const string MagicLearned = "magic.learned";
        public const string MagicLevel = "magic.level";
        public const string ProficiencyLevel = "proficiency.level";
        public const string XpFull = "xp.full";
        public const string StatusStarted = "status.started";
        public const string StatusEnded = "status.ended";
        public const string EnergyLoopFailed = "energy.loop_failed";

        public const string MountStarted = "mount.started";
        public const string MountEnded = "mount.ended";

        public const string ArenaLoaded = "arena.loaded";
        public const string ArenaJoined = "arena.joined";
        public const string ArenaLeft = "arena.left";
        public const string ArenaMatched = "arena.matched";
        public const string ArenaStarted = "arena.started";
        public const string ArenaEnded = "arena.ended";
        public const string ArenaPointsBought = "arena.points_bought";
        public const string ArenaRejected = "arena.rejected";
        public const string ArenaSaveFailed = "arena.save_failed";
        public const string ArenaLoopFailed = "arena.loop_failed";

        public const string NobilityLoaded = "nobility.loaded";
        public const string NobilityDonated = "nobility.donated";
        public const string NobilityRank = "nobility.rank";
        public const string NobilityRejected = "nobility.rejected";

        public const string MoveWalk = "move.walk";
        public const string MoveJump = "move.jump";
        public const string MoveTeleport = "move.teleport";
        public const string MoveRejected = "move.rejected";

        public const string ChatMessage = "chat.message";
        public const string ChatRejected = "chat.rejected";
        public const string GmCommand = "gm.command";
        public const string ChatCommand = "chat.command";

        public const string PacketUnhandled = "packet.unhandled";
        public const string PacketError = "packet.error";
        public const string PacketIn = "packet.in";
        public const string PacketOut = "packet.out";

        public static ILogger For<TSource>(string eventName) =>
            Log.ForContext<TSource>().ForContext("Event", eventName);

        public static ILogger For(Type source, string eventName) =>
            Log.ForContext(source).ForContext("Event", eventName);
    }
}
