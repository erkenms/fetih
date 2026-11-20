namespace Comet.Game.Combat
{
    using System;

    // Formulas as in Albetros, which follows TQ's: no TQ source for 5619 is public.
    public static class Damage
    {
        // Attacking a monster 4 or more levels below: 4 and 5 levels 1.5 times, then 0.7 more
        // for every 5 levels
        public static double LevelBonus(int attackerLevel, int targetLevel)
        {
            int delta = attackerLevel - targetLevel;
            if (delta <= 3)
                return 1;
            return delta <= 5 ? 1.5 : 1 + 0.7 * delta / 5;
        }

        public static uint Melee(int minAttack, int maxAttack, int attackerLevel, int targetLevel,
            int targetDefense, Random random)
        {
            double damage = Roll(minAttack, maxAttack, random) * LevelBonus(attackerLevel, targetLevel);
            return AtLeastOne(damage - targetDefense);
        }

        // Arrows go past defense; dodge takes its percentage off instead
        public static uint Archery(int minAttack, int maxAttack, int attackerLevel, int targetLevel,
            int targetDodge, Random random)
        {
            double damage = Roll(minAttack, maxAttack, random) * LevelBonus(attackerLevel, targetLevel);
            return AtLeastOne(damage * (100 - Math.Clamp(targetDodge, 0, 100)) / 100);
        }

        public static uint MonsterMelee(int minAttack, int maxAttack, int targetDefense, Random random) =>
            AtLeastOne(Roll(minAttack, maxAttack, random) - targetDefense);

        // The chance in percent that an attack lands: monsters' dodge counts a quarter
        public static int HitChance(int targetDodge, bool targetIsMonster) =>
            Math.Clamp(110 - (targetIsMonster ? targetDodge / 4 : targetDodge), 0, 100);

        public static bool Hits(int targetDodge, bool targetIsMonster, Random random) =>
            random.Next(100) < HitChance(targetDodge, targetIsMonster);

        // Experience for damage dealt to a monster, by how many levels the attacker is above it
        public static ulong Experience(uint dealt, int attackerLevel, int monsterLevel)
        {
            int percent = (attackerLevel - monsterLevel) switch
            {
                > 20 => 5,
                > 10 => 10,
                > 5 => 20,
                >= 3 => 70,
                >= 0 => 100,
                >= -5 => 130,
                >= -10 => 150,
                >= -20 => 180,
                _ => 230,
            };
            return dealt == 0 ? 0 : Math.Max(1, dealt * (ulong)percent / 100);
        }

        // Extra experience for the kill: 5% of the monster's life
        public static ulong KillBonus(uint monsterLife) => monsterLife * 5UL / 100;

        // Against players there's no level bonus (Albetros)
        public static uint PlayerMelee(int attack, int targetDefense) => AtLeastOne(attack - targetDefense);

        // Dodge takes its percentage off, and of the rest arrows do 45% to players (Albetros)
        public static uint PlayerArchery(int attack, int targetDodge) =>
            AtLeastOne(attack * (100 - Math.Clamp(targetDodge, 0, 100)) / 100.0 * 0.45);

        public static int Roll(int min, int max, Random random) =>
            max <= min ? min : random.Next(min, max + 1);

        private static uint AtLeastOne(double damage) => (uint)Math.Max(1, damage);
    }
}
