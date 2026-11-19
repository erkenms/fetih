namespace Comet.Game.Items
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Npcs;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgPackage;

    // Putting items and silver in warehouses and taking them out. Runs on the player's own
    // packet queue.
    public static class Storing
    {
        public static async Task<bool> ShowAsync(Client client, uint npcID)
        {
            if (Reach(client, npcID, "show") == null)
                return false;
            var items = client.Warehouses.In(npcID);
            for (int i = 0; i < items.Count; i += ItemsPerPacket)
                await client.SendAsync(new MsgPackage(npcID, PackageAction.Query)
                {
                    Items = items.Skip(i).Take(ItemsPerPacket).ToList(),
                });
            return true;
        }

        public static async Task<bool> DepositAsync(Client client, uint npcID, uint itemID)
        {
            var character = client.Character;
            var item = client.Inventory?.Find(itemID);
            if (Reach(client, npcID, "deposit") == null)
                return false;
            if (item == null)
                return Reject(client, "deposit", npcID, itemID, "not_in_inventory");
            if (!client.Inventory.Remove(item))
                return Reject(client, "deposit", npcID, itemID, "not_in_inventory");
            if (!client.Warehouses.TryAdd(npcID, item))
            {
                client.Inventory.Add(item);
                return Reject(client, "deposit", npcID, itemID, "warehouse_full");
            }

            item.Position = (byte)ItemPosition.Warehouse;
            item.Package = npcID;
            await ItemsRepository.SetStorageAsync(item.ItemID, item.Position, npcID);
            await client.SendAsync(MsgItem.Removed(item.ItemID));
            await ShowAsync(client, npcID);
            LogEvents.For(typeof(Storing), LogEvents.ItemStored).Information(
                "{Name} put item {ItemId} (type {TypeId}) in the warehouse of NPC {NpcId}",
                character.Name, item.ItemID, item.TypeID, npcID);
            return true;
        }

        public static async Task<bool> WithdrawAsync(Client client, uint npcID, uint itemID)
        {
            var character = client.Character;
            if (Reach(client, npcID, "withdraw") == null)
                return false;
            var item = client.Warehouses.Find(npcID, itemID);
            if (item == null)
                return Reject(client, "withdraw", npcID, itemID, "not_in_warehouse");
            if (!client.Inventory.TryAdd(item))
                return Reject(client, "withdraw", npcID, itemID, "inventory_full");

            client.Warehouses.Remove(npcID, item);
            item.Position = (byte)ItemPosition.Inventory;
            item.Package = 0;
            await ItemsRepository.SetStorageAsync(item.ItemID, item.Position, 0);
            await client.SendAsync(new MsgPackage(npcID, PackageAction.Withdraw, item.ItemID));
            await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Add));
            LogEvents.For(typeof(Storing), LogEvents.ItemTakenOut).Information(
                "{Name} took item {ItemId} (type {TypeId}) out of the warehouse of NPC {NpcId}",
                character.Name, item.ItemID, item.TypeID, npcID);
            return true;
        }

        // The answer to the warehouse window asking for the stored silver: the amount in Command
        public static Task ShowSilverAsync(Client client, MsgItem query)
        {
            if (client.Character == null)
                return Task.CompletedTask;
            query.Command = client.Character.StoredSilver;
            return client.SendAsync(query);
        }

        // Positive amounts go into the warehouse, negative ones come out
        public static async Task<bool> MoveSilverAsync(Client client, long amount)
        {
            var character = client.Character;
            if (character == null || amount == 0)
                return false;
            long stored = character.StoredSilver + amount;
            if (stored < 0 || stored > uint.MaxValue || !await Progress.ChangeSilverAsync(client, -amount, "warehouse"))
            {
                LogEvents.For(typeof(Storing), LogEvents.ItemWarehouseRejected).Warning(
                    "{Name} can't move {Amount} silver into the warehouse with {Silver} carried and {Stored} stored",
                    character.Name, amount, character.Silver, character.StoredSilver);
                return false;
            }
            character.StoredSilver = (uint)stored;
            LogEvents.For(typeof(Storing), LogEvents.CharacterStoredSilver).Information(
                "{Name} stored silver {Amount:+#;-#;0} to {Balance}", character.Name, amount, character.StoredSilver);
            await client.SaveAsync("warehouse");
            return true;
        }

        private static Npc Reach(Client client, uint npcID, string use)
        {
            if (client.Character == null || client.Warehouses == null)
                return null;
            var npc = Dialogs.Reach(client, npcID, use);
            if (npc != null && npc.Role != Npc.StorageRole)
            {
                Reject(client, use, npcID, 0, "not_a_warehouse");
                return null;
            }
            return npc;
        }

        private static bool Reject(Client client, string action, uint npcID, uint itemID, string reason)
        {
            LogEvents.For(typeof(Storing), LogEvents.ItemWarehouseRejected).Warning(
                "{Name} can't {Action} item {ItemId} at NPC {NpcId}: {Reason}",
                client.Character?.Name, action, itemID, npcID, reason);
            return false;
        }
    }
}
