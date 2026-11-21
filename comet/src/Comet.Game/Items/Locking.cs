namespace Comet.Game.Items
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using static Comet.Game.Packets.MsgEquipLock;

    // Locked equipment can't be sold, dropped or put in a booth, and only goes to formal
    // trade partners. Unlocking takes five days, as the 5619 client says, and the second
    // password. Runs on the player's own packet queue.
    public static class Locking
    {
        public static readonly TimeSpan UnlockTime = TimeSpan.FromDays(5);

        public static bool IsLocked(DbItem item, DateTime now) => item.Locked && !(item.UnlocksAt <= now);

        public static Task HandleAsync(Client client, MsgEquipLock msg)
        {
            if (client.Character == null)
                return Task.CompletedTask;
            var item = client.Inventory?.Find(msg.ItemID) ??
                client.Equipment?.Items.FirstOrDefault(i => i.ItemID == msg.ItemID);
            switch (msg.Mode)
            {
                case LockMode.Lock: return LockAsync(client, item, msg);
                case LockMode.Unlock: return UnlockAsync(client, item, msg.ItemID);
                default:
                    LogEvents.For(typeof(Locking), LogEvents.PacketUnhandled).Warning(
                        "Unhandled packet {PacketType} action {Action} ({Length} bytes)", msg.Type, msg.Mode, msg.Length);
                    return Task.CompletedTask;
            }
        }

        // Locking an item that is being unlocked keeps it locked
        private static async Task LockAsync(Client client, DbItem item, MsgEquipLock msg)
        {
            var now = DateTime.UtcNow;
            string reason =
                item == null ? "not_carried" :
                !ItemSlots.IsWearable(item.TypeID) ? "not_equipment" :
                IsLocked(item, now) && item.UnlocksAt == null ? "locked" :
                null;
            if (reason != null)
            {
                Reject(client, "lock", msg.ItemID, item?.TypeID ?? 0, reason);
                if (reason == "not_equipment")
                    await Talk.NoticeAsync(client, "Only equipment can be locked!");
                return;
            }

            bool wasUnlocking = IsLocked(item, now);
            item.Locked = true;
            item.UnlocksAt = null;
            await ItemsRepository.SetLockAsync(item.ItemID, true, null);
            await client.SendAsync(msg);
            await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Update));
            LogEvents.For(typeof(Locking), LogEvents.ItemLocked).Information(
                "{Name} locked item {ItemId} (type {TypeId}){Again}",
                client.Character.Name, item.ItemID, item.TypeID, wasUnlocking ? ", stopping its unlock" : "");
        }

        private static async Task UnlockAsync(Client client, DbItem item, uint itemID)
        {
            var now = DateTime.UtcNow;
            string reason =
                item == null ? "not_carried" :
                !IsLocked(item, now) ? "not_locked" :
                item.UnlocksAt != null ? "unlocking" :
                null;
            if (reason != null)
            {
                Reject(client, "unlock", itemID, item?.TypeID ?? 0, reason);
                if (reason == "unlocking")
                    await client.SendAsync(UnlockDate(item));
                return;
            }
            if (!await SecondPassword.EnsureOpenAsync(client, "unlock"))
                return;

            item.UnlocksAt = now + UnlockTime;
            await ItemsRepository.SetLockAsync(item.ItemID, true, item.UnlocksAt);
            await client.SendAsync(UnlockDate(item));
            LogEvents.For(typeof(Locking), LogEvents.ItemUnlockStarted).Information(
                "{Name} asked to unlock item {ItemId} (type {TypeId}); it unlocks at {UnlocksAt:u}",
                client.Character.Name, item.ItemID, item.TypeID, item.UnlocksAt);
        }

        // Before the login sends the items: unlocks that are due take effect. Returns the
        // carried ones, for UnlockedAsync once the client has them.
        public static async Task<List<DbItem>> FinishUnlocksAsync(Client client)
        {
            var now = DateTime.UtcNow;
            var carried = Carried(client).ToHashSet();
            var unlocked = new List<DbItem>();
            foreach (var item in carried.Concat(client.Warehouses?.Items ?? Enumerable.Empty<DbItem>()).ToList())
            {
                if (!item.Locked || IsLocked(item, now))
                    continue;
                item.Locked = false;
                item.UnlocksAt = null;
                await ItemsRepository.SetLockAsync(item.ItemID, false, null);
                if (carried.Contains(item))
                    unlocked.Add(item);
                LogEvents.For(typeof(Locking), LogEvents.ItemUnlocked).Information(
                    "Item {ItemId} (type {TypeId}) of {Name} is unlocked",
                    item.ItemID, item.TypeID, client.Character.Name);
            }
            return unlocked;
        }

        // After the login sent the items: which were just unlocked, and when the others unlock
        public static async Task SendLoginAsync(Client client, IEnumerable<DbItem> unlocked)
        {
            foreach (var item in unlocked)
                await client.SendAsync(new MsgEquipLock(item.ItemID, LockMode.Unlocked));
            foreach (var item in Carried(client).Where(i => i.Locked && i.UnlocksAt != null))
                await client.SendAsync(UnlockDate(item));
        }

        private static IEnumerable<DbItem> Carried(Client client) =>
            (client.Inventory?.Items ?? Enumerable.Empty<DbItem>())
                .Concat(client.Equipment?.Items ?? Enumerable.Empty<DbItem>());

        // The client shows the date as "yy,mm,dd"
        private static MsgEquipLock UnlockDate(DbItem item)
        {
            var date = item.UnlocksAt.Value;
            return new MsgEquipLock(item.ItemID, LockMode.Unlock)
            {
                Flag = 3,
                Value = (uint)(date.Year * 10000 + date.Month * 100 + date.Day),
            };
        }

        private static void Reject(Client client, string action, uint itemID, uint typeID, string reason) =>
            LogEvents.For(typeof(Locking), LogEvents.ItemLockRejected).Warning(
                "{Name} can't {Action} item {ItemId} (type {TypeId}): {Reason}",
                client.Character?.Name, action, itemID, typeID, reason);
    }
}
