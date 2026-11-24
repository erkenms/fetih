namespace Comet.Game.States
{
    using System;
    using System.Linq;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;

    public static class Attributes
    {
        public const ushort PointsPerLevel = 3;

        // What every class starts with, and a rebirth starts over from
        public const ushort StartingStrength = 4, StartingAgility = 6, StartingVitality = 12, StartingSpirit = 0;

        public static void SetStarting(DbCharacter character)
        {
            character.Strength = StartingStrength;
            character.Agility = StartingAgility;
            character.Vitality = StartingVitality;
            character.Spirit = StartingSpirit;
        }

        // A transformed player's is the monster's life
        public static ushort MaxHealth(Client client) => client.Transformation is { } t
            ? (ushort)Math.Min(t.Type.Life, ushort.MaxValue)
            : MaxHealth(client.Character, client.Equipment, client.SubClasses?.MaxHealthBonus() ?? 0);

        // Worn items and Wrangler sub-class add their own health after the trojans' bonus
        public static ushort MaxHealth(DbCharacter character, Equipment equipment, int subClassBonus = 0)
        {
            int health = character.Vitality * 24
                + (character.Strength + character.Agility + character.Spirit) * 3;
            // Trojans past their first promotion
            health = character.CurrentClass switch
            {
                11 => health * 105 / 100,
                12 => health * 108 / 100,
                13 => health * 110 / 100,
                14 => health * 112 / 100,
                15 => health * 115 / 100,
                _ => health
            };
            health += (equipment?.Items.Sum(ItemHealth) ?? 0) + subClassBonus;
            return (ushort)Math.Min(health, ushort.MaxValue);
        }

        // The item type's life, its plus level's from itemadd.ini, and its enchantment
        public static int ItemHealth(DbItem item)
        {
            int health = item.Enchant;
            if (Kernel.ItemTypes.TryGet(item.TypeID, out var type))
                health += type.Life;
            if (item.Plus > 0)
                health += Kernel.ItemAdditions.Of(item.TypeID, item.Plus)?.Life ?? 0;
            return health;
        }

        public static ushort MaxMana(Client client) => MaxMana(client.Character, client.Equipment);

        // A level a point, five a rebirth, nobility rank, and shared guild arsenal battle power
        public static uint BattlePower(DbCharacter character) =>
            character.Level + character.Rebirths * 5u +
            (uint)Nobility.Peerage.RankOf(character.CharacterID) +
            Social.Guilds.BattlePowerOf(character.CharacterID);

        // Worn items add their itemtype mana after the taoists' bonus; itemadd.ini has none
        public static ushort MaxMana(DbCharacter character, Equipment equipment)
        {
            int mana = character.Spirit * 5;
            // Water and fire taoists from their second promotion
            mana *= character.CurrentClass switch
            {
                132 or 142 => 3,
                133 or 143 => 4,
                134 or 144 => 5,
                135 or 145 => 6,
                _ => 1
            };
            mana += equipment?.Items.Sum(i => Kernel.ItemTypes.TryGet(i.TypeID, out var type) ? type.Mana : 0) ?? 0;
            return (ushort)Math.Min(mana, ushort.MaxValue);
        }
    }
}
