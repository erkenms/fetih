namespace Comet.Game.Items
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;

    // Items used from the inventory (MsgItem 4 with slot 0): city scrolls and praying stones. Runs on the player's own packet
    // queue, like Progress.
    public static class Using
    {
        public static async Task<bool> UseAsync(Client client, uint itemID)
        {
            var character = client.Character;
            var item = client.Inventory?.Find(itemID);
            if (character == null || item == null)
                return Reject(client, itemID, 0, "not_in_inventory");

            if (HeavenBlessing.StoneDuration(item.TypeID) is TimeSpan blessing)
                return await PrayAsync(client, item, blessing);

            var scroll = Kernel.MapTypes.ScrollPointOf(item.TypeID);
            if (scroll == null)
                return Reject(client, itemID, item.TypeID, "not_usable");
            if (client.Life.Dead)
                return Reject(client, itemID, item.TypeID, "dead");
            if ((Kernel.MapTypes.Of(character.MapID) & (MapFlags.TeleportDisable | MapFlags.PrisonMap)) != 0)
            {
                await Talk.NoticeAsync(client, "You can't teleport out of this map.");
                return Reject(client, itemID, item.TypeID, "teleport_disabled");
            }

            uint fromMap = character.MapID;
            if (!await Movement.TeleportAsync(client, scroll.MapID, scroll.X, scroll.Y))
                return Reject(client, itemID, item.TypeID, "bad_destination");
            await UseUpAsync(client, item);
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) on map {FromMapId} and went to map {MapId}",
                character.Name, item.ItemID, item.TypeID, fromMap, scroll.MapID);
            return true;
        }

        private static async Task<bool> PrayAsync(Client client, DbItem stone, TimeSpan blessing)
        {
            if (client.Life.Dead)
                return Reject(client, stone.ItemID, stone.TypeID, "dead");
            await UseUpAsync(client, stone);
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) for heaven blessing",
                client.Character.Name, stone.ItemID, stone.TypeID);
            await HeavenBlessing.AddAsync(client, blessing, "praying_stone");
            return true;
        }

        private static async Task UseUpAsync(Client client, DbItem item)
        {
            client.Inventory.Remove(item);
            await ItemsRepository.DeleteAsync(item.ItemID);
            await client.SendAsync(MsgItem.Removed(item.ItemID));
        }

        private static bool Reject(Client client, uint itemID, uint typeID, string reason)
        {
            LogEvents.For(typeof(Using), LogEvents.ItemUseRejected).Warning(
                "{Name} can't use item {ItemId} (type {TypeId}): {Reason}",
                client.Character?.Name, itemID, typeID, reason);
            return false;
        }
    }
}
