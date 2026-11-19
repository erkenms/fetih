namespace Comet.Game.States
{
    using System;
    using Comet.Game.Database.Models;

    public static class Attributes
    {
        public const ushort PointsPerLevel = 3;

        public static ushort MaxHealth(DbCharacter character)
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
            return (ushort)Math.Min(health, ushort.MaxValue);
        }

        public static ushort MaxMana(DbCharacter character)
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
            return (ushort)Math.Min(mana, ushort.MaxValue);
        }
    }
}
