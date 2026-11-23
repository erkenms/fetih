namespace Comet.Game.Items
{
    using System.Threading.Tasks;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;

    // Arrows: a quiver's amount is the arrows left in it. Runs on the player's own queue.
    public static class Quivers
    {
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
