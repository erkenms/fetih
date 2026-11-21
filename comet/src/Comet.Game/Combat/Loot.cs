namespace Comet.Game.Combat
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.States;
    using Comet.Game.World;

    // Meteor and dragonball drop rates are Albetros's
    public static class Loot
    {
        public const int SilverChance = 30;
        public const int PotionChance = 5;
        public const int EquipmentChance = 3;
        public const double MeteorChance = 0.8;
        public const double DragonBallChance = 0.09;

        public const uint Meteor = 1088001;
        public const uint DragonBall = 1088000;

        public static readonly TimeSpan OwnerTime = TimeSpan.FromSeconds(20);

        // Equipment drops reach this many levels below the monster
        private const int EquipmentLevels = 20;

        // Out of 100, the roll below which dropped equipment becomes super (9), elite (8),
        // unique (7) or refined (6)
        private static readonly (int Quality, double Below)[] Upgrades =
        {
            (9, 0.5), (8, 1.5), (7, 4), (6, 10),
        };

        public static async Task DropAsync(Client killer, Monster monster, ushort x, ushort y, Random random)
        {
            if (monster.Type.IsGuard)
                return;
            foreach (var drop in Roll(monster.Type, Kernel.ItemTypes, random))
                await PlaceAsync(killer, monster, x, y, drop);
        }

        public static List<(uint Silver, uint TypeID)> Roll(MonsterType type, ItemTypeTable items, Random random)
        {
            var drops = new List<(uint, uint)>();
            if (type.DropMoney > 0 && random.Next(100) < SilverChance)
                drops.Add(((uint)random.Next((int)Math.Max(1, type.DropMoney / 2), (int)type.DropMoney + 1), 0));
            foreach (var potion in new[] { type.DropHp, type.DropMp })
                if (potion != 0 && random.Next(100) < PotionChance)
                    drops.Add((0, potion));
            if (random.Next(100) < EquipmentChance && Equipment(type.Level, items, random) is uint equipment)
                drops.Add((0, equipment));
            if (random.NextDouble() * 100 < MeteorChance && items.TryGet(Meteor, out _))
                drops.Add((0, Meteor));
            if (random.NextDouble() * 100 < DragonBallChance && items.TryGet(DragonBall, out _))
                drops.Add((0, DragonBall));
            return drops;
        }

        // Qualities 3 to 5 are the normal ones
        public static uint? Equipment(int monsterLevel, ItemTypeTable items, Random random)
        {
            var candidates = items.All.Where(t => ItemSlots.IsMonsterDrop(t.ID) &&
                ItemSlots.Quality(t.ID) is >= 3 and <= 5 &&
                t.RequiredLevel <= monsterLevel && t.RequiredLevel + EquipmentLevels >= monsterLevel).ToList();
            if (candidates.Count == 0)
                return null;
            uint id = candidates[random.Next(candidates.Count)].ID;

            double roll = random.NextDouble() * 100;
            foreach (var (quality, below) in Upgrades)
            {
                if (roll >= below)
                    continue;
                uint better = id / 10 * 10 + (uint)quality;
                return items.TryGet(better, out _) ? better : id;
            }
            return id;
        }

        private static async Task PlaceAsync(Client killer, Monster monster, ushort x, ushort y, (uint Silver, uint TypeID) drop)
        {
            var now = DateTime.UtcNow;
            Database.Models.DbItem item = null;
            if (drop.TypeID != 0)
            {
                if (!Kernel.ItemTypes.TryGet(drop.TypeID, out var type))
                    return;
                item = ItemActions.New(0, type);
                item.Position = (byte)ItemPosition.Ground;
                await ItemsRepository.CreateAsync(item);
            }

            var placed = await Ground.PlaceLootAsync(monster.Map.Id, x, y, new GroundItem
            {
                MapID = monster.Map.Id,
                TypeID = drop.Silver > 0 ? GroundItem.SilverType(drop.Silver) : drop.TypeID,
                Silver = drop.Silver,
                Item = item,
                ExpiresAt = now + Ground.Lifetime,
                OwnerID = killer.ID,
                OwnedUntil = now + OwnerTime,
            });
            if (placed == null)
            {
                if (item != null)
                    await ItemsRepository.DeleteAsync(item.ItemID);
                return;
            }

            LogEvents.For(typeof(Loot), LogEvents.MonsterDrop).Information(
                "{MonsterName} {MonsterId} killed by {Name} dropped item {ItemId} (type {TypeId}) and " +
                "{Silver} silver as ground item {GroundId} on map {MapId} at {X},{Y}",
                monster.Type.Name, monster.Identity, killer.Character.Name, placed.Item?.ItemID ?? 0,
                placed.TypeID, placed.Silver, placed.Identity, placed.MapID, placed.X, placed.Y);
        }
    }
}
