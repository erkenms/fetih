namespace Comet.Game.Items
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;

    // Gear wears by one point a use: a hit taken wears the headwear, necklace and armor; a swing
    // or cast wears the weapons, ring and boots. The client shows durability in hundreds, so a
    // change is sent and saved when the hundreds change. Broken gear (no durability left) adds
    // nothing until repaired: by silver for the share worn, or by five meteors once broken. Each runs on the gear owner's packet queue.
    public static class Durability
    {
        public const int BrokenRepairMeteors = 5;

        private static readonly ItemPosition[] HitWear = { ItemPosition.Headwear, ItemPosition.Necklace, ItemPosition.Armor };
        private static readonly ItemPosition[] AttackWear =
            { ItemPosition.RightHand, ItemPosition.LeftHand, ItemPosition.Ring, ItemPosition.Boots };

        // Gear durability applies to; arrows count arrows with it, and bottles, garments,
        // steeds and crops don't wear
        public static bool Wears(uint typeID) => ItemSlots.IsMonsterDrop(typeID);

        public static bool IsBroken(DbItem item) =>
            item.Amount == 0 && item.AmountLimit > 0 && Wears(item.TypeID);

        public static Task WearOnHitAsync(Client client) => WearAsync(client, HitWear);

        public static Task WearOnAttackAsync(Client client) => WearAsync(client, AttackWear);

        private static async Task WearAsync(Client client, ItemPosition[] positions)
        {
            if (client.Equipment == null || client.Character == null || Training.TrainingGround.CostsNothing(client))
                return;
            foreach (var position in positions)
            {
                var item = client.Equipment[position];
                if (item == null || item.Amount == 0 || !Wears(item.TypeID))
                    continue;
                int before = item.Amount / 100;
                item.Amount--;
                if (item.Amount / 100 == before && item.Amount > 0)
                    continue;

                await ItemsRepository.SetAmountAsync(item.ItemID, item.Amount);
                await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Update));
                if (item.Amount == 0)
                {
                    LogEvents.For(typeof(Durability), LogEvents.ItemBroken).Information(
                        "{Name}'s item {ItemId} (type {TypeId}) in {Position} broke",
                        client.Character.Name, item.ItemID, item.TypeID, position);
                }
                if (item.Amount < 200 && Kernel.ItemTypes.TryGet(item.TypeID, out var type))
                    await Talk.NoticeAsync(client, item.Amount == 0
                        ? $"Your {type.Name} is broken. Repair it."
                        : $"Your {type.Name} is wearing out. Repair it soon.");
            }
        }

        // The item's price for the share worn, less for lower qualities
        public static uint RepairCost(uint price, ushort durability, ushort maxDurability, uint typeID)
        {
            if (maxDurability == 0 || durability >= maxDurability)
                return 0;
            double quality = ItemSlots.Quality(typeID) switch
            {
                9 => 1.125, 8 => 0.975, 7 => 0.9, 6 => 0.825, _ => 0.75,
            };
            return (uint)Math.Max(1, Math.Ceiling(price * (double)(maxDurability - durability) / maxDurability * quality));
        }

        // MsgItem 14: gear in the inventory or worn
        public static async Task<bool> RepairAsync(Client client, uint itemID)
        {
            var character = client.Character;
            var item = client.Inventory?.Find(itemID) ??
                client.Equipment?.Items.FirstOrDefault(i => i.ItemID == itemID);
            if (character == null || item == null)
                return Reject(client, itemID, "not_found");
            if (!Wears(item.TypeID) || !Kernel.ItemTypes.TryGet(item.TypeID, out var type))
                return Reject(client, itemID, "not_repairable");
            if (item.Amount >= item.AmountLimit)
                return Reject(client, itemID, "not_worn");

            string paid;
            if (item.Amount == 0)
            {
                var meteors = client.Inventory.Items.Where(i => i.TypeID == Upgrading.Meteor)
                    .Take(BrokenRepairMeteors).ToList();
                if (meteors.Count < BrokenRepairMeteors)
                {
                    await Talk.NoticeAsync(client, $"Repairing broken gear takes {BrokenRepairMeteors} meteors.");
                    return Reject(client, itemID, "no_meteors");
                }
                foreach (var meteor in meteors)
                {
                    client.Inventory.Remove(meteor);
                    await ItemsRepository.DeleteAsync(meteor.ItemID);
                    await client.SendAsync(MsgItem.Removed(meteor.ItemID));
                }
                paid = $"{BrokenRepairMeteors} meteors";
            }
            else
            {
                uint cost = RepairCost(type.Price, item.Amount, item.AmountLimit, item.TypeID);
                if (!await Progress.ChangeSilverAsync(client, -cost, "repair"))
                {
                    await Talk.NoticeAsync(client, "You don't have enough silver for the repair.");
                    return Reject(client, itemID, "no_silver");
                }
                paid = $"{cost} silver";
            }

            item.Amount = item.AmountLimit;
            await ItemsRepository.SetAmountAsync(item.ItemID, item.Amount);
            await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Update));
            LogEvents.For(typeof(Durability), LogEvents.ItemRepaired).Information(
                "{Name} repaired item {ItemId} (type {TypeId}) for {Paid}",
                character.Name, item.ItemID, item.TypeID, paid);
            return true;
        }

        private static bool Reject(Client client, uint itemID, string reason)
        {
            LogEvents.For(typeof(Durability), LogEvents.ItemRepairRejected).Warning(
                "{Name} can't repair item {ItemId}: {Reason}", client.Character?.Name, itemID, reason);
            return false;
        }
    }
}
