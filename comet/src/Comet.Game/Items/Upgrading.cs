namespace Comet.Game.Items
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;

    // Raising the level or quality of worn gear, as the 5619 client does it: at an NPC, by slot.
    // Upgrades never fail; harder ones cost more gems. Runs on the player's own packet queue,
    // like Equipping.
    public static class Upgrading
    {
        public const uint DragonBall = 1088000;
        public const uint Meteor = 1088001;
        public const byte EmptySocket = Gems.EmptySocket;

        // Gear past this required level can't be raised further with meteors
        public const int MaxUpgradeLevel = 120;

        public enum Upgrade { Level, Quality }

        // The slots the upgrade NPCs offer
        public static readonly ItemPosition[] Slots =
        {
            ItemPosition.Headwear, ItemPosition.Necklace, ItemPosition.Armor, ItemPosition.RightHand,
            ItemPosition.LeftHand, ItemPosition.Ring, ItemPosition.Boots,
        };

        // The gear and what it becomes, or why it can't be upgraded
        public static (Database.Models.DbItem Item, ItemType Next, string Reason) Check(
            Client client, ItemPosition position, Upgrade upgrade, bool capAtMaxLevel)
        {
            var item = client.Equipment?[position];
            if (item == null)
                return (null, null, "nothing_worn");
            var next = upgrade == Upgrade.Level ? NextLevel(item.TypeID) : NextQuality(item.TypeID);
            if (next == null)
                return (item, null, "max");
            if (upgrade == Upgrade.Level && (next.RequiredLevel > client.Character.Level ||
                capAtMaxLevel && next.RequiredLevel > MaxUpgradeLevel))
                return (item, null, "above_level");
            return (item, next, null);
        }

        public static string Explain(string reason, int cost = 0, uint gemType = 0) => reason switch
        {
            "nothing_worn" => "You aren't wearing anything there.",
            "max" => "That item can't be upgraded any further.",
            "above_level" => "You can't upgrade an item past your own level.",
            "no_gems" => $"You need {cost} {(gemType == Meteor ? "meteors" : "dragonballs")} for that.",
            _ => "That can't be upgraded.",
        };

        // Pays the gems and upgrades the gear; why not if it can't
        public static async Task<string> UpgradeAsync(Client client, ItemPosition position, Upgrade upgrade,
            uint gemType, int cost, bool capAtMaxLevel, string npc)
        {
            var character = client.Character;
            var (item, next, reason) = Check(client, position, upgrade, capAtMaxLevel);
            if (reason != null)
                return Reject(client, position, upgrade, reason);
            var gems = client.Inventory.Items.Where(i => i.TypeID == gemType).Take(cost).ToList();
            if (gems.Count < cost)
                return Reject(client, position, upgrade, "no_gems");

            foreach (var gem in gems)
            {
                client.Inventory.Remove(gem);
                await ItemsRepository.DeleteAsync(gem.ItemID);
                await client.SendAsync(MsgItem.Removed(gem.ItemID));
            }
            uint oldType = item.TypeID;
            item.TypeID = next.ID;
            item.Amount = next.Amount;
            item.AmountLimit = next.AmountLimit;
            await ItemsRepository.SetUpgradeAsync(item);
            await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Update));
            await Equipping.ShowLookAsync(client);
            await Equipping.FollowMaximumsAsync(client);

            LogEvents.For(typeof(Upgrading), LogEvents.ItemUpgraded).Information(
                "{Name} upgraded the {Upgrade} of item {ItemId} in {Position} at NPC {Npc} for {Cost} of {GemTypeId}: " +
                "type {OldTypeId} to {TypeId}", character.Name, upgrade.ToString().ToLowerInvariant(), item.ItemID,
                position, npc, cost, gemType, oldType, item.TypeID);
            return null;
        }

        // Normal and lower become refined, then unique, elite and super, at the same level
        public static ItemType NextQuality(uint typeID)
        {
            int current = ItemSlots.Quality(typeID);
            if (!IsUpgradable(typeID) || current >= 9)
                return null;
            uint nextID = typeID - (uint)current + (uint)Math.Max(6, current + 1);
            return Kernel.ItemTypes.TryGet(nextID, out var next) ? next : null;
        }

        public static ItemType NextLevel(uint typeID) =>
            IsUpgradable(typeID) && NextLevelID(typeID) is uint nextID &&
            Kernel.ItemTypes.TryGet(nextID, out var next) ? next : null;

        private static bool IsUpgradable(uint typeID) =>
            ItemSlots.IsMonsterDrop(typeID) && !ItemSlots.IsArrow(typeID);

        // The ID of the same gear a level up: necklaces, rings and boots
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

        // How hard a level upgrade is, in percent: harder for higher levels and better qualities
        public static int LevelChance(uint typeID)
        {
            bool armor = typeID / 1000 is >= 111 and <= 118 or 123 or >= 141 and <= 143 or >= 130 and <= 136 or 900;
            int chance = armor
                ? (typeID % 100 / 10) switch { 5 or 6 => 50, 7 => 30, 8 or 9 => 20, _ => 80 }
                : (typeID % 1000 / 10) switch
                {
                    11 or 12 => 85, 13 or 14 => 80, 15 => 75, 16 => 70, 17 => 65,
                    18 => 60, 19 => 55, 20 => 50, 21 => 45, 22 => 40, _ => 90,
                };
            chance = chance * (typeID % 10) switch { 6 => 90, 7 => 70, 8 => 35, 9 => 20, _ => 100 } / 100;
            return Math.Clamp(chance, 10, 100);
        }

        // How hard a quality upgrade is, in percent: harder for better quality and higher required level
        public static int QualityChance(uint typeID)
        {
            int chance = 85 - Math.Max(0, (int)(typeID % 10) - 5) * 16;
            if (Kernel.ItemTypes.TryGet(typeID, out var type))
                chance -= type.RequiredLevel / 4;
            return Math.Clamp(chance, 20, 100);
        }

        public static int MeteorCost(uint typeID) => 1 + 100 / LevelChance(typeID);

        public static int DragonBallCost(uint typeID) => 1 + 100 / QualityChance(typeID);

        private static string Reject(Client client, ItemPosition position, Upgrade upgrade, string reason)
        {
            LogEvents.For(typeof(Upgrading), LogEvents.ItemUpgradeRejected).Warning(
                "{Name} can't upgrade the {Upgrade} of the item in {Position}: {Reason}",
                client.Character?.Name, upgrade.ToString().ToLowerInvariant(), position, reason);
            return reason;
        }
    }
}
