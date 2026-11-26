namespace Fetih.Game.Items
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Database.Repositories;
    using Fetih.Game.Packets;
    using Fetih.Game.States;

    // Ethereal's Wuxing Oven, in the market, burns a gem into gear for bonus health. The gem's kind and grade
    // set the range the roll falls in (help44); a lower roll keeps the old value, and the gem
    // is spent either way. Runs on the player's own packet queue.
    public static class Enchanting
    {
        public const byte MaxEnchant = 255;

        // The roll for a gem, by its socket value (kind * 10 + grade); null for gems that
        // don't enchant
        public static (int Min, int Max)? Range(byte gem) => (Gems.Kind(gem), Gems.Grade(gem)) switch
        {
            (< 0 or > Gems.Tortoise, _) or (_, 0) => null,
            (_, 1) => (1, 59),
            (Gems.Dragon, 2) => (100, 159),
            (Gems.Rainbow, 2) => (80, 129),
            (Gems.Phoenix or Gems.Moon or Gems.Violet, 2) => (60, 109),
            (Gems.Tortoise or Gems.Fury or Gems.Kylin, 2) => (40, 89),
            (Gems.Dragon, _) => (200, 255),
            (Gems.Rainbow or Gems.Phoenix or Gems.Tortoise, _) => (170, 229),
            (Gems.Moon or Gems.Violet, _) => (140, 199),
            (Gems.Fury, _) => (90, 149),
            (Gems.Kylin, _) => (70, 119),
        };

        public static byte Roll(byte gem, Random random)
        {
            var (min, max) = Range(gem) ?? (0, 0);
            return (byte)random.Next(min, max + 1);
        }

        // MsgItem 28: the gear in the ID field, the gem in Command. The echo carries the roll,
        // which the client compares with the item's enchantment to say whether it went up,
        // so it goes before the item's update.
        public static async Task<bool> EnchantAsync(Client client, MsgItem msg, Random random)
        {
            var character = client.Character;
            var item = client.Inventory?.Find(msg.CharacterID) ??
                client.Equipment?.Items.FirstOrDefault(i => i.ItemID == msg.CharacterID);
            var gem = client.Inventory?.Find(msg.Command);
            if (character == null || item == null)
                return Reject(client, msg.CharacterID, "not_found");
            if (gem == null || gem == item)
                return Reject(client, msg.CharacterID, "gem_not_in_inventory");
            if (Npcs.Dialogs.Reach(client, Npcs.WuxingOven.EtherealID, "enchant") == null &&
                Npcs.Dialogs.Reach(client, Npcs.WuxingOven.OvenID, "enchant") == null)
                return Reject(client, item.ItemID, "too_far");
            if (!ItemSlots.IsMonsterDrop(item.TypeID))
                return Reject(client, item.ItemID, "not_gear");
            byte socket = (byte)(gem.TypeID % 1000);
            if (!Gems.IsGemItem(gem.TypeID) || Range(socket) == null)
                return Reject(client, item.ItemID, "not_a_gem");
            if (item.Enchant >= MaxEnchant)
                return Reject(client, item.ItemID, "at_max");

            client.Inventory.Remove(gem);
            await ItemsRepository.DeleteAsync(gem.ItemID);
            await client.SendAsync(MsgItem.Removed(gem.ItemID));

            byte roll = Roll(socket, random);
            msg.Command = roll;
            await client.SendAsync(msg);
            byte before = item.Enchant;
            if (roll > before)
            {
                item.Enchant = roll;
                await ItemsRepository.SetEnchantAsync(item.ItemID, roll);
                await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Update));
                if (item.Position != (byte)ItemPosition.Inventory)
                    await Equipping.FollowMaximumsAsync(client);
            }
            LogEvents.For(typeof(Enchanting), LogEvents.ItemEnchanted).Information(
                "{Name} enchanted item {ItemId} (type {TypeId}) with gem {Gem}: rolled {Roll}, was {Before}",
                character.Name, item.ItemID, item.TypeID, socket, roll, before);
            return true;
        }

        private static bool Reject(Client client, uint itemID, string reason)
        {
            LogEvents.For(typeof(Enchanting), LogEvents.ItemEnchantRejected).Warning(
                "{Name} can't enchant item {ItemId}: {Reason}", client.Character?.Name, itemID, reason);
            return false;
        }
    }
}
