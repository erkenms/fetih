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

        public const string ItemTypesLoaded = "item.types_loaded";
        public const string ItemCreated = "item.created";
        public const string ItemEquipped = "item.equipped";
        public const string ItemUnequipped = "item.unequipped";
        public const string ItemEquipRejected = "item.equip_rejected";
        public const string ItemDropped = "item.dropped";
        public const string ItemPickedUp = "item.picked_up";
        public const string ItemExpired = "item.expired";
        public const string ItemGroundRejected = "item.ground_rejected";
        public const string ItemBought = "item.bought";
        public const string ItemSold = "item.sold";
        public const string ItemShopRejected = "item.shop_rejected";
        public const string ItemStored = "item.stored";
        public const string ItemTakenOut = "item.taken_out";
        public const string ItemWarehouseRejected = "item.warehouse_rejected";
        public const string ShopsLoaded = "item.shops_loaded";

        public const string TradeRequested = "trade.requested";
        public const string TradeOpened = "trade.opened";
        public const string TradeOffered = "trade.offered";
        public const string TradeCompleted = "trade.completed";
        public const string TradeCancelled = "trade.cancelled";
        public const string TradeRejected = "trade.rejected";

        public const string TeamCreated = "team.created";
        public const string TeamJoined = "team.joined";
        public const string TeamLeft = "team.left";
        public const string TeamDismissed = "team.dismissed";
        public const string TeamRejected = "team.rejected";

        public const string FriendAdded = "friend.added";
        public const string FriendRemoved = "friend.removed";
        public const string FriendRejected = "friend.rejected";
        public const string EnemyRemoved = "enemy.removed";

        public const string MapIndexLoaded = "map.index_loaded";
        public const string MapLoaded = "map.loaded";
        public const string MapLoadFailed = "map.load_failed";
        public const string PortalsLoaded = "map.portals_loaded";

        public const string NpcsLoaded = "npc.loaded";
        public const string NpcTalk = "npc.talk";
        public const string NpcRejected = "npc.rejected";
        public const string TeleportersLoaded = "npc.teleporters_loaded";

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
