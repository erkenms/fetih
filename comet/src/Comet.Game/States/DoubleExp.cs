namespace Comet.Game.States
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Packets;

    // Timed monster-EXP multipliers from EXP potions. Kept in memory per character so the bonus
    // lasts across relogs until expiry or server restart, matching the client's ExpPotion text.
    public static class DoubleExp
    {
        public sealed record Potion(uint Percent, TimeSpan Duration);
        public sealed record Bonus(uint Percent, DateTime Until);

        private static readonly Dictionary<uint, Potion> Potions = new Dictionary<uint, Potion>
        {
            [722700] = new Potion(200, TimeSpan.FromMinutes(10)),   // ExpPotion
            [723017] = new Potion(200, TimeSpan.FromMinutes(60)),   // ExpPotion
            [728775] = new Potion(200, TimeSpan.FromMinutes(30)),   // 2*EXPPotion
            [728776] = new Potion(300, TimeSpan.FromMinutes(30)),   // 3*EXPPotion
            [728777] = new Potion(500, TimeSpan.FromMinutes(30)),   // 5*EXPPotion
            [728810] = new Potion(500, TimeSpan.FromMinutes(60)),   // EliteEXPPotion
            [728201] = new Potion(500, TimeSpan.FromMinutes(120)),  // SuperExpPotion
            [720393] = new Potion(300, TimeSpan.FromMinutes(120)),  // ExpPill
            [720394] = new Potion(500, TimeSpan.FromMinutes(120)),  // ExpAmrita
            [722703] = new Potion(200, TimeSpan.FromMinutes(30)),   // VitalityPill
            [722955] = new Potion(200, TimeSpan.FromMinutes(60)),   // SupremeSpanishRice
        };

        private static readonly ConcurrentDictionary<uint, Bonus> Active = new ConcurrentDictionary<uint, Bonus>();

        public static bool TryGetPotion(uint typeID, out Potion potion) => Potions.TryGetValue(typeID, out potion);

        // False if a stronger multiplier is already running; same rate extends its remaining time
        public static bool Apply(uint characterID, uint percent, TimeSpan duration, DateTime now)
        {
            while (true)
            {
                if (Active.TryGetValue(characterID, out var current))
                {
                    if (current.Until > now)
                    {
                        if (current.Percent > percent)
                            return false;
                        var next = current.Percent == percent
                            ? new Bonus(percent, current.Until + duration)
                            : new Bonus(percent, now + duration);
                        if (Active.TryUpdate(characterID, next, current))
                            return true;
                        continue;
                    }
                    if (Active.TryUpdate(characterID, new Bonus(percent, now + duration), current))
                        return true;
                    continue;
                }
                if (Active.TryAdd(characterID, new Bonus(percent, now + duration)))
                    return true;
            }
        }

        public static Bonus Current(uint characterID, DateTime now) =>
            Active.TryGetValue(characterID, out var bonus) && bonus.Until > now ? bonus : null;

        public static uint SecondsLeft(uint characterID, DateTime now) =>
            Current(characterID, now) is { } bonus
                ? (uint)Math.Ceiling((bonus.Until - now).TotalSeconds)
                : 0;

        public static ulong Scale(uint characterID, ulong experience, DateTime now) =>
            Current(characterID, now) is { } bonus ? experience * bonus.Percent / 100 : experience;

        public static Task SendAsync(Client client, DateTime now) =>
            Current(client.ID, now) is { } bonus
                ? client.SendAsync(MsgUserAttrib.DoubleExpOf(client.ID,
                    (uint)Math.Ceiling((bonus.Until - now).TotalSeconds), bonus.Percent))
                : Task.CompletedTask;

        public static async Task ExpireAsync(Client client, DateTime now)
        {
            if (Active.TryGetValue(client.ID, out var bonus) && bonus.Until <= now &&
                Active.TryRemove(new KeyValuePair<uint, Bonus>(client.ID, bonus)))
                await client.SendAsync(MsgUserAttrib.DoubleExpOf(client.ID, 0, 0));
        }

        public static void Clear(uint characterID) => Active.TryRemove(characterID, out _);
    }
}
