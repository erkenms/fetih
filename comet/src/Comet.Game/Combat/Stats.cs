namespace Comet.Game.Combat
{
    using System;
    using System.Collections.Generic;
    using Comet.Game.Items;
    using Comet.Game.States;

    // Gems and blessings are percentages kept apart from the flat numbers, as the client's stat
    // window shows them. Worn gear's refinery states add to the sub-classes' percentages; a
    // magic defense refinery raises magic defense by its percent.
    public sealed record Stats(int MinAttack, int MaxAttack, int Defense, int Dodge, int Range,
        int AttackSpeed, bool Bow, int MagicAttack = 0, int MagicDefense = 0)
    {
        public const int UnarmedSpeed = 1000;

        public int AttackPercent { get; init; }
        public int MagicPercent { get; init; }
        public int TortoisePercent { get; init; }
        public int BlessPercent { get; init; }
        public int CriticalStrike { get; init; }
        public int SkillCriticalStrike { get; init; }
        public int Immunity { get; init; }
        public int Penetration { get; init; }
        public int Detoxication { get; init; }
        public int Block { get; init; }
        public int Breakthrough { get; init; }
        public int Counteraction { get; init; }

        public int MinDamage => this.MinAttack * (100 + this.AttackPercent) / 100;
        public int MaxDamage => this.MaxAttack * (100 + this.AttackPercent) / 100;
        public int MagicDamage => this.MagicAttack * (100 + this.MagicPercent) / 100;

        // 0 is a miss; a hit that landed still does at least 1
        public uint Taken(uint damage) => damage == 0 ? 0 : Math.Max(1u,
            (uint)(damage * (100L - this.TortoisePercent) * (100L - this.BlessPercent) / 10000));

        public static Stats Of(Client client)
        {
            var character = client.Character;
            int min = character.Strength, max = character.Strength, defense = 0, dodge = 0;
            int magicAttack = 0, magicDefense = 0, attackPercent = 0, magicPercent = 0, tortoise = 0, bless = 0;
            var refined = new Dictionary<RefineryKind, int>();
            var now = DateTime.UtcNow;
            if (client.Equipment != null)
            {
                foreach (var item in client.Equipment.Items)
                {
                    if (ItemSlots.IsArrow(item.TypeID) || ItemSlots.IsSteed(item.TypeID) ||
                        Durability.IsBroken(item) || !Kernel.ItemTypes.TryGet(item.TypeID, out var type))
                        continue;
                    min += type.MinAttack;
                    max += type.MaxAttack;
                    defense += type.Defense;
                    dodge += type.Dodge;
                    magicAttack += type.MagicAttack;
                    magicDefense += type.MagicDefense;
                    if (item.Plus > 0 && Kernel.ItemAdditions.Of(item.TypeID, item.Plus) is { } plus)
                    {
                        min += plus.MinAttack;
                        max += plus.MaxAttack;
                        defense += plus.Defense;
                        dodge += plus.Speed;
                        magicAttack += plus.MagicAttack;
                        magicDefense += plus.MagicDefense;
                    }

                    foreach (byte gem in new[] { item.Gem1, item.Gem2 })
                    {
                        switch (Gems.Kind(gem))
                        {
                            case Gems.Dragon: attackPercent += Gems.Percent(gem); break;
                            case Gems.Phoenix: magicPercent += Gems.Percent(gem); break;
                            case Gems.Tortoise: tortoise += Gems.Percent(gem); break;
                        }
                    }
                    bless += item.Bless;
                    var (kind, power) = Refinery.Active(item, now);
                    if (kind != RefineryKind.None)
                        refined[kind] = refined.GetValueOrDefault(kind) + (int)power;
                }
            }

            var weapon = client.Equipment?[ItemPosition.RightHand];
            ItemType weaponType = null;
            if (weapon != null)
                Kernel.ItemTypes.TryGet(weapon.TypeID, out weaponType);
            int range = weaponType == null ? 1 : Math.Max(1, (int)weaponType.AttackRange);
            int speed = weaponType == null || weaponType.AttackSpeed == 0 ? UnarmedSpeed : weaponType.AttackSpeed;
            bool bow = weapon != null && ItemSlots.IsBow(weapon.TypeID);
            if (client.Transformation is { } transformed)
                (min, max, defense, dodge) = (transformed.Type.AttackMin, transformed.Type.AttackMax,
                    transformed.Type.Defense, transformed.Type.Dodge);
            var sub = client.SubClasses;
            if (sub != null)
            {
                int attackBonus = sub.AttackBonus();
                min += attackBonus;
                max += attackBonus;
                magicAttack += sub.MagicAttackBonus();
            }
            dodge = Magic.Buffs.Dodge(client.Effects, dodge, now);
            magicDefense = magicDefense * (100 + refined.GetValueOrDefault(RefineryKind.MagicDefense)) / 100;
            return new Stats(min, max, defense, dodge, range, speed, bow, magicAttack, magicDefense)
            {
                AttackPercent = attackPercent,
                MagicPercent = magicPercent,
                TortoisePercent = Math.Min(Gems.MaxTortoisePercent, tortoise),
                BlessPercent = Math.Min(100, bless),
                CriticalStrike = (sub?.CriticalStrikePercent() ?? 0) + refined.GetValueOrDefault(RefineryKind.CriticalStrike),
                SkillCriticalStrike = (sub?.SkillCriticalStrikePercent() ?? 0) +
                    refined.GetValueOrDefault(RefineryKind.SkillCriticalStrike),
                Immunity = (sub?.ImmunityPercent() ?? 0) + refined.GetValueOrDefault(RefineryKind.Immunity),
                Penetration = (sub?.PenetrationPercent() ?? 0) + refined.GetValueOrDefault(RefineryKind.Penetration),
                Detoxication = (sub?.DetoxicationPercent() ?? 0) + refined.GetValueOrDefault(RefineryKind.Detoxication),
                Block = refined.GetValueOrDefault(RefineryKind.Block),
                Breakthrough = refined.GetValueOrDefault(RefineryKind.Breakthrough),
                Counteraction = refined.GetValueOrDefault(RefineryKind.Counteraction),
            };
        }
    }
}
