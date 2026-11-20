namespace Comet.Game.States
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    // Status bits (MsgUserAttrib 25, MsgPlayer 22), as Albetros (5517) names them. The client
    // draws each from ini/statuseffect.ini.
    public static class StatusFlags
    {
        public const ulong Flashing = 0x1;
        public const ulong Poisoned = 0x2;
        public const ulong XpFull = 0x10;
        public const ulong Accuracy = 0x80;
        public const ulong Shield = 0x100;
        public const ulong Stigma = 0x200;
        public const ulong Dead = 0x400;
        public const ulong RedName = 0x4000;
        public const ulong BlackName = 0x8000;
        public const ulong Superman = 0x40000;
        public const ulong Cyclone = 0x800000;
        public const ulong Riding = 1UL << 50;
    }

    // Statuses that last a while: XP full, and those spells give, with the spell's power.
    // Read by other players' queues (MsgPlayer), so only changed under the lock.
    public sealed class TimedStatus
    {
        private readonly object Sync = new object();
        private readonly Dictionary<ulong, (DateTime Until, int Power)> Until =
            new Dictionary<ulong, (DateTime, int)>();

        public ulong Status(DateTime now)
        {
            lock (this.Sync)
                return this.Until.Where(x => x.Value.Until > now).Aggregate(0UL, (all, x) => all | x.Key);
        }

        public bool Has(ulong flag, DateTime now) => this.Power(flag, now) != null;

        // The power the status was given with, or null if it isn't on
        public int? Power(ulong flag, DateTime now)
        {
            lock (this.Sync)
                return this.Until.TryGetValue(flag, out var status) && status.Until > now ? status.Power : null;
        }

        // True if the status wasn't on before
        public bool Set(ulong flag, DateTime until, DateTime now, int power = 0)
        {
            lock (this.Sync)
            {
                bool started = !this.Until.TryGetValue(flag, out var was) || was.Until <= now;
                this.Until[flag] = (until, power);
                return started;
            }
        }

        // Adds time to a status that's on, while less than the cap is left
        public bool Extend(ulong flag, TimeSpan by, TimeSpan cap, DateTime now)
        {
            lock (this.Sync)
            {
                if (!this.Until.TryGetValue(flag, out var status) || status.Until <= now || status.Until - now >= cap)
                    return false;
                this.Until[flag] = (status.Until + by, status.Power);
                return true;
            }
        }

        public void Clear()
        {
            lock (this.Sync)
                this.Until.Clear();
        }

        public bool Remove(ulong flag)
        {
            lock (this.Sync)
                return this.Until.Remove(flag);
        }

        // Takes away the statuses that have run out and returns them
        public ulong Expire(DateTime now)
        {
            lock (this.Sync)
            {
                ulong ended = 0;
                foreach (var (flag, status) in this.Until.ToList())
                {
                    if (status.Until > now)
                        continue;
                    this.Until.Remove(flag);
                    ended |= flag;
                }
                return ended;
            }
        }
    }
}
