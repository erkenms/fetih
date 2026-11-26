namespace Fetih.Game.Items
{
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Database.Repositories;
    using Fetih.Game.Packets;
    using Fetih.Game.States;

    // Runs on the player's own packet queue
    public static class Stacking
    {
        // amount of a stack's items go to a new stack in a free slot
        public static async Task<bool> SplitAsync(Client client, uint itemID, uint amount)
        {
            var character = client.Character;
            var stack = client.Inventory?.Find(itemID);
            if (character == null || stack == null)
                return Reject(client, itemID, amount, "not_in_inventory");
            if (amount == 0 || amount >= stack.StackAmount)
                return Reject(client, itemID, amount, "bad_amount");
            if (client.Inventory.FreeSlots == 0)
                return Reject(client, itemID, amount, "inventory_full");

            var part = new Database.Models.DbItem
            {
                CharacterID = character.CharacterID,
                TypeID = stack.TypeID,
                Position = (byte)ItemPosition.Inventory,
                Amount = stack.Amount,
                AmountLimit = stack.AmountLimit,
                StackAmount = (ushort)amount,
                Color = stack.Color,
            };
            await ItemsRepository.CreateAsync(part);
            if (!client.Inventory.Split(stack, part, (ushort)amount))
            {
                await ItemsRepository.DeleteAsync(part.ItemID);
                return Reject(client, itemID, amount, "changed");
            }
            await ItemsRepository.SetStackAmountAsync(stack.ItemID, stack.StackAmount);
            await client.SendAsync(new MsgItemInfo(stack, MsgItemInfo.ItemAction.Update));
            await client.SendAsync(new MsgItemInfo(part, MsgItemInfo.ItemAction.Add));
            LogEvents.For(typeof(Stacking), LogEvents.ItemSplit).Information(
                "{Name} split {Amount} of item {ItemId} (type {TypeId}) into item {NewItemId}, {StackAmount} left",
                character.Name, amount, stack.ItemID, stack.TypeID, part.ItemID, stack.StackAmount);
            return true;
        }

        private static bool Reject(Client client, uint itemID, uint amount, string reason)
        {
            LogEvents.For(typeof(Stacking), LogEvents.ItemSplitRejected).Warning(
                "{Name} can't split {Amount} of item {ItemId}: {Reason}", client.Character?.Name, amount, itemID, reason);
            return false;
        }
    }
}
