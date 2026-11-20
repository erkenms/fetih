namespace Comet.Game.Magic
{
    using System;

    // Stamina and the XP bar, kept in memory only and changed on the player's own queue.
    // A login starts with full stamina and an empty XP bar (Albetros).
    public sealed class Energy
    {
        public const byte MaxStamina = 100;
        public const byte MaxXp = 100;

        public byte Stamina { get; set; } = MaxStamina;
        public byte Xp { get; set; }
        public DateTime NextXpAt { get; set; }

        // Monsters killed during the running XP skill; the client shows it on each kill
        public uint KoCount { get; set; }
    }
}
