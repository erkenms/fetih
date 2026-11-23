namespace Comet.Game.Items
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;

    // Runs on the player's own packet queue, like Progress
    public static class ItemActions
    {
        // Null if the inventory is full
        public static async Task<DbItem> GiveAsync(Client client, ItemType type, string reason, byte plus = 0)
        {
            var character = client.Character;
            if (character == null || client.Inventory == null || client.Inventory.FreeSlots == 0)
                return null;

            var item = New(character.CharacterID, type);
            item.Plus = plus;
            await ItemsRepository.CreateAsync(item);
            client.Inventory.Add(item);
            await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Add));
            LogEvents.For(typeof(ItemActions), LogEvents.ItemCreated).Information(
                "{Name} got {ItemName} (item {ItemId}, type {TypeId}; {Reason})",
                character.Name, type.Name, item.ItemID, type.ID, reason);
            return item;
        }

        // Takes count items of the type from the inventory, or none if it has fewer
        public static async Task<bool> TakeAsync(Client client, uint typeID, int count, string reason)
        {
            var character = client.Character;
            if (character == null || client.Inventory == null)
                return false;
            var items = client.Inventory.Items.Where(i => i.TypeID == typeID).Take(count).ToList();
            if (items.Count < count)
                return false;
            foreach (var item in items)
            {
                client.Inventory.Remove(item);
                await ItemsRepository.DeleteAsync(item.ItemID);
                await client.SendAsync(MsgItem.Removed(item.ItemID));
                LogEvents.For(typeof(ItemActions), LogEvents.ItemSpent).Information(
                    "{Name} spent item {ItemId} (type {TypeId}; {Reason})", character.Name, item.ItemID, typeID, reason);
            }
            return true;
        }

        public static DbItem New(uint characterID, ItemType type) => new DbItem
        {
            CharacterID = characterID,
            TypeID = type.ID,
            Position = (byte)ItemPosition.Inventory,
            Amount = type.Amount,
            AmountLimit = type.AmountLimit,
            Gem1 = type.Gem1,
            Gem2 = type.Gem2,
            Color = 3,
        };
    }
}
