namespace Comet.Game.Items
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;

    // Arrows: a quiver's amount is the arrows left in it. Runs on the player's own queue.
    public static class Quivers
    {
        // The 5619 client's itemtype.dat has 500 for all three plain quivers
        private static readonly Dictionary<uint, ushort> Sizes = new Dictionary<uint, ushort>
        {
            [1050000] = 20, // LuckyArrow
            [1050001] = 100, // IronArrow
            [1050002] = 500, // SpeedArrow
        };

        public static ItemType Resize(ItemType type) =>
            Sizes.TryGetValue(type.ID, out var size) ? type with { Amount = size, AmountLimit = size } : type;

        // Takes arrows from the quiver in the left hand; an empty quiver is gone
        public static async Task UseAsync(Client client, Database.Models.DbItem quiver, ushort count)
        {
            quiver.Amount = (ushort)(quiver.Amount > count ? quiver.Amount - count : 0);
            if (quiver.Amount > 0)
            {
                await ItemsRepository.SetAmountAsync(quiver.ItemID, quiver.Amount);
                await client.SendAsync(new MsgItemInfo(quiver, MsgItemInfo.ItemAction.Update));
                return;
            }

            client.Equipment.Clear(ItemPosition.LeftHand);
            await ItemsRepository.DeleteAsync(quiver.ItemID);
            await client.SendAsync(MsgItem.Unequipped(quiver.ItemID, ItemPosition.LeftHand));
            await client.SendAsync(MsgItem.Removed(quiver.ItemID));
            await client.SendAsync(MsgItem.DisplayGears(client.ID, client.Equipment));
            await Movement.SendToViewAsync(client, new MsgPlayer(client));
        }
    }
}
