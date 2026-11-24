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
        public static Task<DbItem> GiveAsync(Client client, ItemType type, string reason, byte plus = 0) =>
            GiveAsync(client, type, reason, item => item.Plus = plus);

        // A plain item of a stackable type goes on a stack with room before taking a slot
        public static async Task<DbItem> GiveAsync(Client client, ItemType type, string reason, System.Action<DbItem> shape)
        {
            var character = client.Character;
            if (character == null || client.Inventory == null)
                return null;

            var item = New(character.CharacterID, type);
            shape(item);
            if (item.Plus == 0 && client.Inventory.StackOne(type.ID, StackLimit(type.ID)) is { } stack)
            {
                await ItemsRepository.SetStackAmountAsync(stack.ItemID, stack.StackAmount);
                await client.SendAsync(new MsgItemInfo(stack, MsgItemInfo.ItemAction.Update));
                LogEvents.For(typeof(ItemActions), LogEvents.ItemCreated).Information(
                    "{Name} got {ItemName} (on stack {ItemId} of {StackAmount}, type {TypeId}; {Reason})",
                    character.Name, type.Name, stack.ItemID, stack.StackAmount, type.ID, reason);
                return stack;
            }
            if (client.Inventory.FreeSlots == 0)
                return null;

            await ItemsRepository.CreateAsync(item);
            client.Inventory.Add(item);
            await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Add));
            LogEvents.For(typeof(ItemActions), LogEvents.ItemCreated).Information(
                "{Name} got {ItemName} (item {ItemId}, type {TypeId}; {Reason})",
                character.Name, type.Name, item.ItemID, type.ID, reason);
            return item;
        }

        // Takes count items of the type from the inventory, stacks counting all they hold, or
        // none if it has fewer
        public static async Task<bool> TakeAsync(Client client, uint typeID, int count, string reason)
        {
            var character = client.Character;
            var taken = character == null ? null : client.Inventory?.Take(typeID, count);
            if (taken == null)
                return false;
            foreach (var item in taken)
            {
                await SaveTakenAsync(client, item);
                LogEvents.For(typeof(ItemActions), LogEvents.ItemSpent).Information(
                    "{Name} spent from item {ItemId} (type {TypeId}, {StackAmount} left; {Reason})",
                    character.Name, item.ItemID, typeID, client.Inventory.Contains(item) ? item.StackAmount : 0, reason);
            }
            return true;
        }

        // One of the item, off its stack or the whole item
        public static async Task<bool> SpendOneAsync(Client client, DbItem item)
        {
            if (client.Inventory == null || !client.Inventory.TakeOne(item))
                return false;
            await SaveTakenAsync(client, item);
            return true;
        }

        // Stores and shows what Inventory.TakeOne or Take did to the item
        public static async Task SaveTakenAsync(Client client, DbItem item)
        {
            if (client.Inventory.Contains(item))
            {
                await ItemsRepository.SetStackAmountAsync(item.ItemID, item.StackAmount);
                await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Update));
            }
            else
            {
                await ItemsRepository.DeleteAsync(item.ItemID);
                await client.SendAsync(MsgItem.Removed(item.ItemID));
            }
        }

        // How many of the type an inventory slot holds. Arrows count theirs in Amount instead.
        public static ushort StackLimit(uint typeID) =>
            !ItemSlots.IsArrow(typeID) && Kernel.ItemTypes.TryGet(typeID, out var type) && type.StackLimit > 1
                ? (ushort)System.Math.Min(type.StackLimit, ushort.MaxValue) : (ushort)1;

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
