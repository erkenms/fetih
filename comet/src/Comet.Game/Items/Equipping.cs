namespace Comet.Game.Items
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;

    // Runs on the player's own packet queue, like Progress
    public static class Equipping
    {
        public static async Task<bool> EquipAsync(Client client, uint itemID, ItemPosition position)
        {
            var character = client.Character;
            var item = client.Inventory?.Find(itemID);
            if (character == null || item == null)
                return Reject(client, itemID, position, "not_in_inventory");
            if (!Kernel.ItemTypes.TryGet(item.TypeID, out var type))
                return Reject(client, itemID, position, "unknown_type");
            if (!ItemSlots.Fits(type.ID, position))
                return Reject(client, itemID, position, "wrong_slot");
            var unmet = Requirements.Unmet(character, type);
            if (unmet != null)
                return Reject(client, itemID, position, unmet);

            var equipment = client.Equipment;
            var removed = new List<DbItem>();
            if (equipment[position] != null)
                removed.Add(equipment[position]);

            var left = equipment[ItemPosition.LeftHand];
            if (position == ItemPosition.RightHand && ItemSlots.IsTwoHanded(type.ID) && left != null &&
                !(ItemSlots.IsBow(type.ID) && ItemSlots.IsArrow(left.TypeID)))
                removed.Add(left);

            if (position == ItemPosition.LeftHand)
            {
                uint right = equipment.TypeAt(ItemPosition.RightHand);
                bool fits = ItemSlots.IsArrow(type.ID) ? ItemSlots.IsBow(right) : ItemSlots.IsOneHanded(right);
                if (!fits)
                    return Reject(client, itemID, position, "needs_right_hand");
            }

            // The equipped item frees its inventory slot
            if (removed.Count - 1 > client.Inventory.FreeSlots)
                return Reject(client, itemID, position, "inventory_full");

            if (!client.Inventory.Remove(item))
                return Reject(client, itemID, position, "not_in_inventory");
            var moves = new List<(DbItem Item, ItemPosition From)>();
            foreach (var old in removed)
            {
                moves.Add((old, (ItemPosition)old.Position));
                equipment.Clear((ItemPosition)old.Position);
                old.Position = (byte)ItemPosition.Inventory;
                client.Inventory.Add(old);
            }
            item.Position = (byte)position;
            equipment.Set(position, item);

            await ItemsRepository.SetPositionsAsync(
                removed.Append(item).Select(i => (i.ItemID, i.Position)).ToList());
            foreach (var (old, from) in moves)
                await client.SendAsync(MsgItem.Unequipped(old.ItemID, from));
            await client.SendAsync(MsgItem.Equipped(item.ItemID, position));
            if (position == ItemPosition.Steed)
                await Riding.DismountAsync(client, "steed_changed");
            if (position == ItemPosition.Crop)
                await Riding.FollowCropAsync(client);
            await ShowLookAsync(client);
            await FollowMaximumsAsync(client);

            LogEvents.For(typeof(Equipping), LogEvents.ItemEquipped).Debug(
                "{Name} equipped {ItemName} (item {ItemId}) in {Position}; {RemovedCount} items " +
                "went back to the inventory", character.Name, type.Name, item.ItemID, position, removed.Count);
            return true;
        }

        public static async Task<bool> UnequipAsync(Client client, uint itemID, ItemPosition position)
        {
            var character = client.Character;
            var item = client.Equipment?[position];
            if (character == null || item == null || item.ItemID != itemID)
                return Reject(client, itemID, position, "not_equipped");
            if (client.Inventory.FreeSlots == 0)
                return Reject(client, itemID, position, "inventory_full");

            client.Equipment.Clear(position);
            item.Position = (byte)ItemPosition.Inventory;
            client.Inventory.Add(item);

            await ItemsRepository.SetPositionsAsync(new[] { (item.ItemID, item.Position) });
            await client.SendAsync(MsgItem.Unequipped(item.ItemID, position));
            if (position == ItemPosition.Steed)
                await Riding.DismountAsync(client, "steed_changed");
            if (position == ItemPosition.Crop)
                await Riding.FollowCropAsync(client);
            await ShowLookAsync(client);
            await FollowMaximumsAsync(client);

            LogEvents.For(typeof(Equipping), LogEvents.ItemUnequipped).Debug(
                "{Name} took off item {ItemId} from {Position}", character.Name, item.ItemID, position);
            return true;
        }

        public static async Task ShowLookAsync(Client client)
        {
            await client.SendAsync(MsgItem.DisplayGears(client.ID, client.Equipment));
            await Movement.SendToViewAsync(client, new MsgPlayer(client));
        }

        // Worn items change only the maximums, or putting them on would refill health and
        // mana; health and mana above a lower maximum come down to it. Maximums go first, so
        // the client doesn't cut to an older one.
        public static async Task FollowMaximumsAsync(Client client)
        {
            var character = client.Character;
            ushort maxHealth = Attributes.MaxHealth(client), maxMana = Attributes.MaxMana(client);
            var attributes = new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.MaxHealth, maxHealth)
                .Append(MsgUserAttrib.AttributeType.MaxMana, maxMana);
            if (character.HealthPoints > maxHealth)
            {
                character.HealthPoints = maxHealth;
                attributes.Append(MsgUserAttrib.AttributeType.Health, maxHealth);
            }
            if (character.ManaPoints > maxMana)
            {
                character.ManaPoints = maxMana;
                attributes.Append(MsgUserAttrib.AttributeType.Mana, maxMana);
            }
            await client.SendAsync(attributes);
        }

        private static bool Reject(Client client, uint itemID, ItemPosition position, string reason)
        {
            LogEvents.For(typeof(Equipping), LogEvents.ItemEquipRejected).Warning(
                "{Name} can't move item {ItemId} to or from {Position}: {Reason}",
                client.Character?.Name, itemID, position, reason);
            return false;
        }
    }
}
