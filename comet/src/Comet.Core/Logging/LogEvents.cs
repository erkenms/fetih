namespace Comet.Core.Logging
{
    using System;
    using Serilog;

    public static class LogEvents
    {
        public const string ServerStart = "server.start";
        public const string ServerStop = "server.stop";
        public const string MetricsListening = "metrics.listening";

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
        public const string SessionLogoutFailed = "session.logout_failed";
        public const string SessionReplaced = "session.replaced";
        public const string SessionPing = "session.ping";
        public const string SessionCheatReport = "session.cheat_report";
        public const string CharacterCreated = "character.created";
        public const string CharacterCreateFailed = "character.create_failed";
        public const string CharacterDeleted = "character.deleted";
        public const string CharacterDeleteRejected = "character.delete_rejected";
        public const string CharacterRelocated = "character.relocated";
        public const string CharacterSaved = "character.saved";
        public const string CharacterSaveFailed = "character.save_failed";
        public const string CharacterSaveRound = "character.save_round";
        public const string CharacterLevel = "character.level";
        public const string CharacterAllotted = "character.allotted";
        public const string CharacterAllotRejected = "character.allot_rejected";
        public const string CharacterPromoted = "character.promoted";
        public const string CharacterPromotionRejected = "character.promotion_rejected";
        public const string CharacterReborn = "character.reborn";
        public const string CharacterRebirthRejected = "character.rebirth_rejected";
        public const string CharacterReincarnated = "character.reincarnated";
        public const string CharacterReincarnateRejected = "character.reincarnate_rejected";
        public const string CharacterReallotted = "character.reallotted";
        public const string CharacterRenamed = "character.renamed";
        public const string CharacterRenameRejected = "character.rename_rejected";
        public const string CharacterSilver = "character.silver";
        public const string CharacterAvatar = "character.avatar";
        public const string CharacterAvatarRejected = "character.avatar_rejected";
        public const string CharacterBody = "character.body";
        public const string CharacterConquerPoints = "character.conquer_points";
        public const string CharacterStoredSilver = "character.stored_silver";
        public const string CharacterDied = "character.died";
        public const string CharacterRevived = "character.revived";
        public const string CharacterReviveRejected = "character.revive_rejected";
        public const string CharacterBlessed = "character.blessed";
        public const string LuckChanged = "character.luck_changed";
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
        public const string HonorRewardsLoaded = "arena.honor_rewards_loaded";
        public const string SubClassesLoaded = "subclass.loaded";
        public const string SubClassLearned = "subclass.learned";
        public const string SubClassUpgraded = "subclass.upgraded";
        public const string SubClassPromoted = "subclass.promoted";
        public const string SubClassActivated = "subclass.activated";
        public const string SubClassRejected = "subclass.rejected";
        public const string SubClassStudyPoints = "subclass.study_points";
        public const string ItemLocked = "item.locked";
        public const string ItemUnlockStarted = "item.unlock_started";
        public const string ItemUnlocked = "item.unlocked";
        public const string ItemLockRejected = "item.lock_rejected";
        public const string ItemUpgraded = "item.upgraded";
        public const string ItemUpgradeRejected = "item.upgrade_rejected";
        public const string ItemDegraded = "item.degraded";
        public const string ItemDegradeRejected = "item.degrade_rejected";
        public const string ItemEmbedded = "item.embedded";
        public const string ItemEmbedRejected = "item.embed_rejected";
        public const string ItemBroken = "item.broken";
        public const string ItemRepaired = "item.repaired";
        public const string ItemRepairRejected = "item.repair_rejected";
        public const string ItemSpent = "item.spent";
        public const string ItemSplit = "item.split";
        public const string ItemSplitRejected = "item.split_rejected";
        public const string ItemEnchanted = "item.enchanted";
        public const string ItemEnchantRejected = "item.enchant_rejected";
        public const string ItemComposed = "item.composed";
        public const string ItemComposeRejected = "item.compose_rejected";
        public const string ItemRefined = "item.refined";
        public const string ItemRefineRejected = "item.refine_rejected";
        public const string ItemRefineryExpired = "item.refinery_expired";
        public const string ItemStabilized = "item.stabilized";
        public const string RefineryLoaded = "refinery.loaded";
        public const string MiningStarted = "mining.started";
        public const string MiningRejected = "mining.rejected";
        public const string MiningFound = "mining.found";

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
        public const string TeamExperience = "team.experience";

        public const string FriendAdded = "friend.added";
        public const string FriendRemoved = "friend.removed";
        public const string FriendRejected = "friend.rejected";
        public const string EnemyAdded = "enemy.added";
        public const string EnemyRemoved = "enemy.removed";

        public const string TradePartnerRequested = "trade_partner.requested";
        public const string TradePartnerAdded = "trade_partner.added";
        public const string TradePartnerRemoved = "trade_partner.removed";
        public const string TradePartnerRejected = "trade_partner.rejected";

        public const string MentorsLoaded = "mentor.loaded";
        public const string MentorRequested = "mentor.requested";
        public const string MentorAdded = "mentor.added";
        public const string MentorRemoved = "mentor.removed";
        public const string MentorRewardClaimed = "mentor.reward_claimed";
        public const string MentorRejected = "mentor.rejected";

        public const string MarriageProposed = "marriage.proposed";
        public const string MarriageMarried = "marriage.married";
        public const string MarriageDivorced = "marriage.divorced";
        public const string MarriageRejected = "marriage.rejected";

        public const string GuildsLoaded = "guild.loaded";
        public const string GuildCreated = "guild.created";
        public const string GuildDisbanded = "guild.disbanded";
        public const string GuildJoined = "guild.joined";
        public const string GuildLeft = "guild.left";
        public const string GuildRank = "guild.rank";
        public const string GuildDonated = "guild.donated";
        public const string GuildRelation = "guild.relation";
        public const string GuildAnnounced = "guild.announced";
        public const string GuildArsenalUnlocked = "guild.arsenal_unlocked";
        public const string GuildArsenalEnhanced = "guild.arsenal_enhanced";
        public const string GuildArsenalInscribed = "guild.arsenal_inscribed";
        public const string GuildArsenalRemoved = "guild.arsenal_removed";
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
        public const string PkDetained = "pk.detained";
        public const string PkRedeemed = "pk.redeemed";
        public const string PkClaimed = "pk.claimed";
        public const string PkDetainRejected = "pk.detain_rejected";

        public const string DataReloaded = "data.reloaded";
        public const string DataReloadRejected = "data.reload_rejected";

        public const string MapIndexLoaded = "map.index_loaded";
        public const string MapLoaded = "map.loaded";
        public const string MapLoadFailed = "map.load_failed";
        public const string PortalsLoaded = "map.portals_loaded";
        public const string MapTypesLoaded = "map.types_loaded";

        public const string NpcsLoaded = "npc.loaded";
        public const string NpcTalk = "npc.talk";
        public const string NpcRejected = "npc.rejected";
        public const string TeleportersLoaded = "npc.teleporters_loaded";
        public const string TalkersLoaded = "npc.talkers_loaded";

        public const string MonstersLoaded = "monster.loaded";
        public const string MonsterKilled = "monster.killed";
        public const string MonsterRespawned = "monster.respawned";
        public const string MonsterDrop = "monster.drop";
        public const string MonsterLoopFailed = "monster.loop_failed";
        public const string DropsLoaded = "monster.drops_loaded";
        public const string BossesLoaded = "boss.loaded";
        public const string BossSpawned = "boss.spawned";

        public const string CombatHit = "combat.hit";
        public const string CombatRejected = "combat.rejected";

        public const string MagicTypesLoaded = "magic.types_loaded";
        public const string MagicCast = "magic.cast";
        public const string MagicRejected = "magic.rejected";
        public const string MagicLearned = "magic.learned";
        public const string MagicLearnRejected = "magic.learn_rejected";
        public const string MagicForgotten = "magic.forgotten";
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
        public const string ArenaSeasonReset = "arena.season_reset";
        public const string ArenaRejected = "arena.rejected";
        public const string ArenaSaveFailed = "arena.save_failed";
        public const string ArenaLoopFailed = "arena.loop_failed";

        public const string NobilityLoaded = "nobility.loaded";
        public const string NobilityDonated = "nobility.donated";
        public const string NobilityRank = "nobility.rank";
        public const string NobilityRejected = "nobility.rejected";

        public const string FlowersLoaded = "flowers.loaded";
        public const string FlowerSent = "flowers.sent";
        public const string FlowerRejected = "flowers.rejected";
        public const string GuildWarLoaded = "guildwar.loaded";
        public const string GuildWarStarted = "guildwar.started";
        public const string GuildWarEnded = "guildwar.ended";
        public const string GuildWarCaptured = "guildwar.captured";
        public const string GuildWarGateChanged = "guildwar.gate_changed";
        public const string GuildWarPrizeClaimed = "guildwar.prize_claimed";
        public const string GuildWarLoopFailed = "guildwar.loop_failed";
        public const string ClanWarLoaded = "clanwar.loaded";
        public const string ClanWarSignedUp = "clanwar.signed_up";
        public const string ClanWarCancelled = "clanwar.cancelled";
        public const string ClanWarAbandoned = "clanwar.abandoned";
        public const string ClanWarStarted = "clanwar.started";
        public const string ClanWarEntered = "clanwar.entered";
        public const string ClanWarKill = "clanwar.kill";
        public const string ClanWarEnded = "clanwar.ended";
        public const string ClanWarRevenue = "clanwar.revenue";
        public const string ClanWarRejected = "clanwar.rejected";
        public const string ClanWarLoopFailed = "clanwar.loop_failed";
        public const string EventsLoaded = "event.loaded";
        public const string EventEdge = "event.edge";
        public const string EventLoopFailed = "event.loop_failed";
        public const string ElitePkLoaded = "elitepk.loaded";
        public const string ElitePkOpened = "elitepk.opened";
        public const string ElitePkSignedUp = "elitepk.signed_up";
        public const string ElitePkLeft = "elitepk.left";
        public const string ElitePkStarted = "elitepk.started";
        public const string ElitePkRound = "elitepk.round";
        public const string ElitePkMatchEnded = "elitepk.match_ended";
        public const string ElitePkEnded = "elitepk.ended";
        public const string ElitePkRewardClaimed = "elitepk.reward_claimed";
        public const string ElitePkRejected = "elitepk.rejected";
        public const string ElitePkFailed = "elitepk.failed";
        public const string TeamPkLoaded = "teampk.loaded";
        public const string TeamPkOpened = "teampk.opened";
        public const string TeamPkSignedUp = "teampk.signed_up";
        public const string TeamPkLeft = "teampk.left";
        public const string TeamPkStarted = "teampk.started";
        public const string TeamPkRound = "teampk.round";
        public const string TeamPkMatchEnded = "teampk.match_ended";
        public const string TeamPkEnded = "teampk.ended";
        public const string TeamPkRewardClaimed = "teampk.reward_claimed";
        public const string TeamPkRejected = "teampk.rejected";
        public const string TeamPkFailed = "teampk.failed";
        public const string CaptureTheFlagLoaded = "ctf.loaded";
        public const string CaptureTheFlagStarted = "ctf.started";
        public const string CaptureTheFlagEntered = "ctf.entered";
        public const string CaptureTheFlagPickedUp = "ctf.picked_up";
        public const string CaptureTheFlagPlanted = "ctf.planted";
        public const string CaptureTheFlagDropped = "ctf.dropped";
        public const string CaptureTheFlagOccupied = "ctf.occupied";
        public const string CaptureTheFlagKill = "ctf.kill";
        public const string CaptureTheFlagExploit = "ctf.exploit";
        public const string CaptureTheFlagRewarded = "ctf.rewarded";
        public const string CaptureTheFlagEnded = "ctf.ended";
        public const string CaptureTheFlagRejected = "ctf.rejected";
        public const string CaptureTheFlagFailed = "ctf.failed";
        public const string GuildPkLoaded = "guildpk.loaded";
        public const string GuildPkContestOpened = "guildpk.contest_opened";
        public const string GuildPkContestStarted = "guildpk.contest_started";
        public const string GuildPkContestEnded = "guildpk.contest_ended";
        public const string GuildPkEntered = "guildpk.entered";
        public const string GuildPkMatchesOpened = "guildpk.matches_opened";
        public const string GuildPkMatchesStarted = "guildpk.matches_started";
        public const string GuildPkMatchEnded = "guildpk.match_ended";
        public const string GuildPkMatchesEnded = "guildpk.matches_ended";
        public const string GuildPkFlagCaptured = "guildpk.flag_captured";
        public const string GuildPkPrizeClaimed = "guildpk.prize_claimed";
        public const string GuildPkRejected = "guildpk.rejected";
        public const string GuildPkFailed = "guildpk.failed";
        public const string GuildRewardLoaded = "guildreward.loaded";
        public const string GuildRewardAdded = "guildreward.added";
        public const string GuildRewardSplit = "guildreward.split";
        public const string GuildRewardPaid = "guildreward.paid";
        public const string GuildRewardRejected = "guildreward.rejected";
        public const string GuildRewardFailed = "guildreward.failed";

        public const string MoveWalk = "move.walk";
        public const string MoveJump = "move.jump";
        public const string MoveTeleport = "move.teleport";
        public const string MoveRejected = "move.rejected";

        public const string ChatMessage = "chat.message";
        public const string ChatRejected = "chat.rejected";
        public const string BroadcastQueued = "broadcast.queued";
        public const string BroadcastUrgent = "broadcast.urgent";
        public const string BroadcastSent = "broadcast.sent";
        public const string BroadcastRejected = "broadcast.rejected";
        public const string BroadcastsLoaded = "broadcast.loaded";
        public const string BroadcastLoopFailed = "broadcast.loop_failed";
        public const string MessageBoardLoaded = "message_board.loaded";
        public const string MessageBoardPosted = "message_board.posted";
        public const string MessageBoardDeleted = "message_board.deleted";
        public const string MessageBoardRejected = "message_board.rejected";
        public const string GmCommand = "gm.command";
        // Admin RPC calls, from the web
        public const string GmKick = "gm.kick";
        public const string GmMute = "gm.mute";
        public const string GmJail = "gm.jail";
        public const string GmRelease = "gm.release";
        public const string GmBan = "gm.ban";
        public const string GmUnban = "gm.unban";
        public const string GmTeleport = "gm.teleport";
        public const string GmGiveItem = "gm.give_item";
        public const string GmTakeItem = "gm.take_item";
        public const string GmSilver = "gm.silver";
        public const string GmConquerPoints = "gm.conquer_points";
        public const string GmLevel = "gm.level";
        public const string GmMessage = "gm.message";
        public const string GmBroadcast = "gm.broadcast";
        public const string GmGuildWar = "gm.guild_war";
        public const string GmDisbandGuild = "gm.disband_guild";
        public const string GmDeletePost = "gm.delete_post";
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
