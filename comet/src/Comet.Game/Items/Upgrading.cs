namespace Comet.Game.Items
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;

    // A dragonball dropped on gear in the inventory raises its quality (MsgItem 19), a meteor its
    // level (MsgItem 20). The odds are TQ's, as World Conquer Online has them: a failure halves the
    // item's durability, and either way one try in a hundred opens a socket. Runs on the player's
    // own packet queue, like Equipping.
    public static class Upgrading
    {
        public const uint DragonBall = 1088000;
        public const uint Meteor = 1088001;
        public const uint MeteorTear = 1088002;
        public const byte EmptySocket = 255;

        // Gear above this required level can't be raised further with meteors
        public const int MaxUpgradeLevel = 120;

        public static Task<bool> ImproveAsync(Client client, uint itemID, uint gemID) =>
            UpgradeAsync(client, itemID, gemID, quality: true);

        public static Task<bool> LevelUpAsync(Client client, uint itemID, uint gemID) =>
            UpgradeAsync(client, itemID, gemID, quality: false);

        private static async Task<bool> UpgradeAsync(Client client, uint itemID, uint gemID, bool quality)
        {
            var character = client.Character;
            var item = client.Inventory?.Find(itemID);
            var gem = client.Inventory?.Find(gemID);
            string upgrade = quality ? "quality" : "level";
            if (character == null || item == null || gem == null || item == gem)
                return Reject(client, itemID, upgrade, "not_in_inventory");
            if (client.Life.Dead)
                return Reject(client, itemID, upgrade, "dead");
            if (quality ? gem.TypeID != DragonBall : gem.TypeID is not (Meteor or MeteorTear))
                return Reject(client, itemID, upgrade, "wrong_gem");
            if (item.Amount < item.AmountLimit)
            {
                await Talk.NoticeAsync(client, "Repair the item before upgrading it.");
                return Reject(client, itemID, upgrade, "needs_repair");
            }

            var next = quality ? NextQuality(item.TypeID) : NextLevel(item.TypeID);
            if (next == null)
            {
                await Talk.NoticeAsync(client, quality
                    ? "This item's quality can't be raised."
                    : "This item's level can't be raised.");
                return Reject(client, itemID, upgrade, "max");
            }

            var (newType, chance) = next.Value;
            client.Inventory.Remove(gem);
            await ItemsRepository.DeleteAsync(gem.ItemID);
            await client.SendAsync(MsgItem.Removed(gem.ItemID));

            uint oldType = item.TypeID;
            bool success = Random.Shared.NextDouble() * 100 < chance;
            if (success)
            {
                item.TypeID = newType.ID;
                item.Amount = newType.Amount;
                item.AmountLimit = newType.AmountLimit;
            }
            else
            {
                item.Amount /= 2;
            }

            bool socket = Random.Shared.Next(100) == 0 && OpenSocket(item);
            await ItemsRepository.SetUpgradeAsync(item);
            await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Update));
            await Talk.NoticeAsync(client, success
                ? quality ? "The item's quality has been raised." : "The item's level has been raised."
                : "The upgrade failed and the item lost durability.");
            if (socket)
                await Talk.NoticeAsync(client, "The upgrade opened a socket in the item.");

            LogEvents.For(typeof(Upgrading), LogEvents.ItemUpgraded).Information(
                "{Name} used {GemTypeId} on item {ItemId} for {Upgrade} at {Chance}%: {Result}, " +
                "type {OldTypeId} to {TypeId}, durability {Amount}/{AmountLimit}, socket opened {Socket}",
                character.Name, gem.TypeID, item.ItemID, upgrade, chance, success ? "success" : "failure",
                oldType, item.TypeID, item.Amount, item.AmountLimit, socket);
            return success;
        }

        private static bool OpenSocket(DbItem item)
        {
            if (item.Gem1 == 0)
                item.Gem1 = EmptySocket;
            else if (item.Gem2 == 0)
                item.Gem2 = EmptySocket;
            else
                return false;
            return true;
        }

        // Normal (3 to 5) becomes refined at once; refined, unique and elite take their chances,
        // smaller by a point for each required level above 70.
        public static (ItemType Type, double Chance)? NextQuality(uint typeID)
        {
            int current = ItemSlots.Quality(typeID);
            if (!IsUpgradable(typeID) || current < 3 || current >= 9 ||
                !Kernel.ItemTypes.TryGet(typeID, out var type))
                return null;
            uint nextID = typeID - (uint)current + (uint)Math.Max(6, current + 1);
            if (!Kernel.ItemTypes.TryGet(nextID, out var next))
                return null;

            double chance = current switch { 6 => 50, 7 => 33, 8 => 20, _ => 100 };
            if (current >= 6 && type.RequiredLevel > 70)
                chance = Math.Floor(chance * (100 - (type.RequiredLevel - 70)) / 100);
            return (next, Math.Max(1, chance));
        }

        public static (ItemType Type, double Chance)? NextLevel(uint typeID)
        {
            if (!IsUpgradable(typeID) || NextLevelID(typeID) is not uint nextID ||
                !Kernel.ItemTypes.TryGet(nextID, out var next) || next.RequiredLevel >= MaxUpgradeLevel)
                return null;
            return (next, LevelChance(nextID));
        }

        private static bool IsUpgradable(uint typeID) =>
            ItemSlots.IsMonsterDrop(typeID) && !ItemSlots.IsArrow(typeID);

        // The ID of the same gear a level up, as CptSky worked it out: necklaces, rings and boots
        // skip levels the client has no item for; the rest go up by 10.
        public static uint? NextLevelID(uint typeID)
        {
            uint sort = typeID / 100000, type = typeID / 10000, subtype = typeID / 1000;
            uint level = typeID % 1000 / 10;
            uint step = 0;
            if (sort == 1 && (subtype is 120 or 121 || type is 15 or 16))
            {
                if (type == 12 && level < 8 || type == 15 && subtype != 152 && level > 0 && level < 21 ||
                    type == 15 && subtype == 152 && level >= 4 && level < 22 || type == 16 && level > 0 && level < 21)
                    step = 20;
                else if (type == 12 && (level == 8 || level >= 21) || type == 15 && subtype != 152 && (level == 0 || level >= 21) ||
                    type == 15 && subtype == 152 && level >= 22 || type == 16 && level >= 21)
                    step = 10;
                else if (type == 12 && level >= 9 && level < 21 || type == 15 && subtype == 152 && level == 1)
                    step = 30;
            }
            else if (sort == 1 && (type is 11 or 13 or 14 || subtype == 123) || sort is 4 or 5 or 6 ||
                sort == 9 && typeID % 100 / 10 != 30)
            {
                step = 10;
            }

            if (step == 0 || (typeID + step) / 1000 != subtype)
                return null;
            uint next = typeID + step;
            // Backswords have no item at these levels
            if (sort is 4 or 5 or 6 && next / 10 is 42103 or 42105 or 42109 or 42111)
                next += 10;
            return next / 1000 == subtype ? next : null;
        }

        // By the level of the gear it becomes and by its quality
        public static double LevelChance(uint nextID)
        {
            uint type = nextID / 10000, subtype = nextID / 1000;
            bool armor = type is 11 or 13 or 14 or 90 || subtype == 123;
            double chance = armor
                ? (nextID % 100 / 10) switch { 6 => 40, 7 => 30, 8 => 20, 9 => 15, _ => 100 }
                : (nextID % 1000 / 10) switch
                {
                    12 => 90, 13 => 85, 14 => 80, 15 => 75, 16 => 70, 17 => 65,
                    18 => 60, 19 => 55, 20 => 50, 21 => 45, 22 => 40, _ => 100,
                };
            return chance * (nextID % 10) switch { 6 => 0.9, 7 => 0.7, 8 => 0.3, 9 => 0.1, _ => 1 };
        }

        private static bool Reject(Client client, uint itemID, string upgrade, string reason)
        {
            LogEvents.For(typeof(Upgrading), LogEvents.ItemUpgradeRejected).Warning(
                "{Name} can't upgrade the {Upgrade} of item {ItemId}: {Reason}",
                client.Character?.Name, upgrade, itemID, reason);
            return false;
        }
    }
}
