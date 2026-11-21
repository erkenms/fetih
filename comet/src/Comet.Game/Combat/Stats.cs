namespace Comet.Game.Combat
{
    using System;
    using Comet.Game.Items;
    using Comet.Game.States;

    // A player's fighting numbers: strength adds to attack, worn items add their own and their
    // plus levels'. Arrows are ammunition and broken gear adds nothing. Gems and blessings are percentages kept
    // apart, as the stat window shows them: dragon gems raise attack, phoenix gems magic attack,
    // tortoise gems and blessed items cut damage taken.
    public sealed record Stats(int MinAttack, int MaxAttack, int Defense, int Dodge, int Range,
        int AttackSpeed, bool Bow, int MagicAttack = 0, int MagicDefense = 0)
    {
        public const int UnarmedSpeed = 1000;

        public int AttackPercent { get; init; }
        public int MagicPercent { get; init; }
        public int TortoisePercent { get; init; }
        public int BlessPercent { get; init; }

        public int MinDamage => this.MinAttack * (100 + this.AttackPercent) / 100;
        public int MaxDamage => this.MaxAttack * (100 + this.AttackPercent) / 100;
        public int MagicDamage => this.MagicAttack * (100 + this.MagicPercent) / 100;

        // What's left of a hit on this player; a hit that landed still does at least 1
        public uint Taken(uint damage) => damage == 0 ? 0 : Math.Max(1u,
            (uint)(damage * (100L - this.TortoisePercent) * (100L - this.BlessPercent) / 10000));

        public static Stats Of(Client client)
        {
            var character = client.Character;
            int min = character.Strength, max = character.Strength, defense = 0, dodge = 0;
            int magicAttack = 0, magicDefense = 0, attackPercent = 0, magicPercent = 0, tortoise = 0, bless = 0;
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
                }
            }

            var weapon = client.Equipment?[ItemPosition.RightHand];
            ItemType weaponType = null;
            if (weapon != null)
                Kernel.ItemTypes.TryGet(weapon.TypeID, out weaponType);
            int range = weaponType == null ? 1 : Math.Max(1, (int)weaponType.AttackRange);
            int speed = weaponType == null || weaponType.AttackSpeed == 0 ? UnarmedSpeed : weaponType.AttackSpeed;
            bool bow = weapon != null && ItemSlots.IsBow(weapon.TypeID);
            return new Stats(min, max, defense, dodge, range, speed, bow, magicAttack, magicDefense)
            {
                AttackPercent = attackPercent,
                MagicPercent = magicPercent,
                TortoisePercent = Math.Min(Gems.MaxTortoisePercent, tortoise),
                BlessPercent = Math.Min(100, bless),
            };
        }
    }
}
