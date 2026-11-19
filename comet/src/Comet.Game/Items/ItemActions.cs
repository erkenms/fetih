namespace Comet.Game.Items
{
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
        public static async Task<DbItem> GiveAsync(Client client, ItemType type, string reason)
        {
            var character = client.Character;
            if (character == null || client.Inventory == null || client.Inventory.FreeSlots == 0)
                return null;

            var item = New(character.CharacterID, type);
            await ItemsRepository.CreateAsync(item);
            client.Inventory.Add(item);
            await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Add));
            LogEvents.For(typeof(ItemActions), LogEvents.ItemCreated).Information(
                "{Name} got {ItemName} (item {ItemId}, type {TypeId}; {Reason})",
                character.Name, type.Name, item.ItemID, type.ID, reason);
            return item;
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
