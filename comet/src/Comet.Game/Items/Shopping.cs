namespace Comet.Game.Items
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Npcs;
    using Comet.Game.Packets;
    using Comet.Game.States;

    // Buying from and selling to shopkeepers. Runs on the player's own packet queue.
    public static class Shopping
    {
        // Shopkeepers buy back at a third of the price, less for worn items
        public const uint SellDivisor = 3;

        public static async Task<bool> BuyAsync(Client client, uint npcID, uint typeID, uint amount)
        {
            var character = client.Character;
            if (character == null || client.Inventory == null)
                return false;
            amount = amount == 0 ? 1 : amount;
            var shop = Find(client, npcID, "buy");
            if (shop == null)
                return Reject(client, "buy", npcID, typeID, "no_shop");
            if (!shop.Sells(typeID) || !Kernel.ItemTypes.TryGet(typeID, out var type))
                return Reject(client, "buy", npcID, typeID, "not_sold_here");
            if (shop.MoneyType != Shop.Silver)
                return Reject(client, "buy", npcID, typeID, "not_for_silver");
            if (amount > client.Inventory.FreeSlots)
                return Reject(client, "buy", npcID, typeID, "inventory_full");
            long cost = (long)type.Price * amount;
            if (cost > character.Silver)
                return Reject(client, "buy", npcID, typeID, "not_enough_silver");

            await Progress.ChangeSilverAsync(client, -cost, "buy");
            for (int i = 0; i < amount; i++)
            {
                var item = await ItemActions.GiveAsync(client, type, "buy");
                LogEvents.For(typeof(Shopping), LogEvents.ItemBought).Information(
                    "{Name} bought {ItemName} (item {ItemId}, type {TypeId}) from NPC {NpcId} for {Price} silver",
                    character.Name, type.Name, item?.ItemID ?? 0, typeID, npcID, type.Price);
            }
            return true;
        }

        public static async Task<bool> SellAsync(Client client, uint npcID, uint itemID)
        {
            var character = client.Character;
            if (character == null || client.Inventory == null)
                return false;
            var item = client.Inventory.Find(itemID);
            if (item == null)
                return Reject(client, "sell", npcID, 0, "not_in_inventory");
            if (Find(client, npcID, "sell") == null)
                return Reject(client, "sell", npcID, item.TypeID, "no_shop");
            if (Locking.IsLocked(item, DateTime.UtcNow))
            {
                await Chat.Talk.NoticeAsync(client, "This equipment is locked. It can't be sold.");
                return Reject(client, "sell", npcID, item.TypeID, "locked");
            }
            if (Social.Guilds.IsInscribed(item.ItemID))
            {
                await Chat.Talk.NoticeAsync(client, "The inscribed equipment cannot be sold.");
                return Reject(client, "sell", npcID, item.TypeID, "inscribed");
            }

            uint price = Kernel.ItemTypes.TryGet(item.TypeID, out var type) ? SellPrice(type.Price, item.Amount, item.AmountLimit) : 0;
            if ((long)character.Silver + price > uint.MaxValue)
                return Reject(client, "sell", npcID, item.TypeID, "too_much_silver");

            if (!client.Inventory.Remove(item))
                return Reject(client, "sell", npcID, item.TypeID, "not_in_inventory");
            await ItemsRepository.DeleteAsync(item.ItemID);
            await client.SendAsync(MsgItem.Removed(item.ItemID));
            await Progress.ChangeSilverAsync(client, price, "sell");
            LogEvents.For(typeof(Shopping), LogEvents.ItemSold).Information(
                "{Name} sold item {ItemId} (type {TypeId}) to NPC {NpcId} for {Price} silver",
                character.Name, item.ItemID, item.TypeID, npcID, price);
            return true;
        }

        public static uint SellPrice(uint price, ushort durability, ushort maxDurability)
        {
            ulong value = price / SellDivisor;
            if (maxDurability > 0 && durability < maxDurability)
                value = value * durability / maxDurability;
            return (uint)value;
        }

        private static Shop Find(Client client, uint npcID, string use)
        {
            var shop = Kernel.Shops.Find(npcID);
            return shop != null && Dialogs.Reach(client, npcID, use) != null ? shop : null;
        }

        private static bool Reject(Client client, string action, uint npcID, uint typeID, string reason)
        {
            LogEvents.For(typeof(Shopping), LogEvents.ItemShopRejected).Warning(
                "{Name} can't {Action} type {TypeId} at NPC {NpcId}: {Reason}",
                client.Character?.Name, action, typeID, npcID, reason);
            return false;
        }
    }
}
