namespace Comet.Game.Items
{
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;

    // Gems into the sockets of gear in the inventory, and out of them; a removed gem is lost.
    // Thunder gems go only in Heaven Fans (201), glory gems only in Star Towers (202), and
    // talismans take no other gems. Emptying the first socket moves the second gem into it.
    // Runs on the player's own packet queue, like Upgrading.
    public static class Embedding
    {
        public static async Task<bool> HandleAsync(Client client, MsgGemEmbed msg)
        {
            var character = client.Character;
            var item = client.Inventory?.Find(msg.ItemID);
            if (character == null || item == null)
                return Reject(client, msg, "not_in_inventory");
            if (client.Life.Dead)
                return Reject(client, msg, "dead");
            if (msg.Socket is not (1 or 2))
                return Reject(client, msg, "bad_socket");

            byte gemSocket = 0;
            uint gemType = 0;
            if (msg.Action == MsgGemEmbed.EmbedAction.Embed)
            {
                var gem = client.Inventory.Find(msg.GemID);
                if (gem == null || gem == item)
                    return Reject(client, msg, "gem_not_in_inventory");
                gemType = gem.TypeID;
                gemSocket = (byte)(gem.TypeID % 1000);
                if (!Gems.IsGemItem(gem.TypeID) || Gems.Kind(gemSocket) < 0)
                    return Reject(client, msg, "not_a_gem");
                if (!Fits(item.TypeID, Gems.Kind(gemSocket)))
                {
                    await Talk.NoticeAsync(client, "This gem doesn't fit that item.");
                    return Reject(client, msg, "wrong_item");
                }

                // The second socket fills the first while that one's empty
                bool first = msg.Socket == 1 || item.Gem1 == Gems.EmptySocket;
                byte current = first ? item.Gem1 : item.Gem2;
                if (current == Gems.NoSocket)
                {
                    await Talk.NoticeAsync(client, "That item has no such socket.");
                    return Reject(client, msg, "no_socket");
                }
                if (current != Gems.EmptySocket)
                {
                    await Talk.NoticeAsync(client, "That socket already holds a gem.");
                    return Reject(client, msg, "socket_full");
                }

                client.Inventory.Remove(gem);
                await ItemsRepository.DeleteAsync(gem.ItemID);
                await client.SendAsync(MsgItem.Removed(gem.ItemID));
                if (first)
                    item.Gem1 = gemSocket;
                else
                    item.Gem2 = gemSocket;
            }
            else if (msg.Action == MsgGemEmbed.EmbedAction.Remove)
            {
                byte current = msg.Socket == 1 ? item.Gem1 : item.Gem2;
                if (current is Gems.NoSocket or Gems.EmptySocket)
                    return Reject(client, msg, "no_gem");
                gemSocket = current;
                if (msg.Socket == 2)
                    item.Gem2 = Gems.EmptySocket;
                else if (item.Gem2 is not (Gems.NoSocket or Gems.EmptySocket))
                    (item.Gem1, item.Gem2) = (item.Gem2, Gems.EmptySocket);
                else
                    item.Gem1 = Gems.EmptySocket;
            }
            else
            {
                return Reject(client, msg, "unknown_action");
            }

            await ItemsRepository.SetUpgradeAsync(item);
            await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Update));
            await client.SendAsync(msg);
            LogEvents.For(typeof(Embedding), LogEvents.ItemEmbedded).Information(
                "{Name} {Action} gem {Gem} (type {GemTypeId}) in socket {Socket} of item {ItemId}; " +
                "sockets now {Gem1} and {Gem2}", character.Name, msg.Action.ToString(), gemSocket, gemType,
                msg.Socket, item.ItemID, item.Gem1, item.Gem2);
            return true;
        }

        private static bool Fits(uint itemType, int gemKind)
        {
            uint sort = itemType / 1000;
            bool talismanGem = gemKind is Gems.Thunder or Gems.Glory;
            return sort switch
            {
                201 => gemKind == Gems.Thunder,
                202 => gemKind == Gems.Glory,
                _ => !talismanGem,
            };
        }

        private static bool Reject(Client client, MsgGemEmbed msg, string reason)
        {
            LogEvents.For(typeof(Embedding), LogEvents.ItemEmbedRejected).Warning(
                "{Name} can't {Action} a gem in socket {Socket} of item {ItemId}: {Reason}",
                client.Character?.Name, msg.Action.ToString(), msg.Socket, msg.ItemID, reason);
            return false;
        }
    }
}
