namespace Comet.Game.States
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    // Status bits (MsgUserAttrib 25, MsgPlayer 22), 128 of them. The client draws each from
    // ini/statuseffect.ini, whose first column is the bit.
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
        public const ulong IronShirt = 0x80000;
        public const ulong Invisible = 0x400000;
        public const ulong Cyclone = 0x800000;
        public const ulong Dodge = 0x4000000;
        public const ulong Fly = 0x8000000;
        public const ulong Intensify = 0x10000000;
        public const ulong Bless = 0x40000000;
        public const ulong HeavenBlessing = 1UL << 33;
        public const ulong NoPotion = 1UL << 44;
        public const ulong ShurikenVortex = 1UL << 46;
        public const ulong FatalStrike = 1UL << 47;
        public const ulong Riding = 1UL << 50;

        public static readonly UInt128 AzureShield = UInt128.One << 93;
        public static readonly UInt128 TyrantAura = UInt128.One << 98;
        public static readonly UInt128 FendAura = UInt128.One << 100;
        public static readonly UInt128 MetalAura = UInt128.One << 102;
        public static readonly UInt128 WoodAura = UInt128.One << 104;
        public static readonly UInt128 WaterAura = UInt128.One << 106;
        public static readonly UInt128 FireAura = UInt128.One << 108;
        public static readonly UInt128 EarthAura = UInt128.One << 110;
        public static readonly UInt128 SoulShackle = UInt128.One << 111;
        public static readonly UInt128 Oblivion = UInt128.One << 112;

        // Magictype's status number n is bit n + 1 here (Cannon Barrage's 119 is the bit the
        // client turns attacks into the cannon at)
        public static readonly UInt128 Fatigue = UInt128.One << 119;
        public static readonly UInt128 CannonBarrage = UInt128.One << 120;
        public static readonly UInt128 BlackbeardsRage = UInt128.One << 121;
    }

    // Statuses that last a while: XP full, and those spells give, with the spell's power.
    // Read by other players' queues (MsgPlayer), so only changed under the lock.
    public sealed class TimedStatus
    {
        private readonly object Sync = new object();
        private readonly Dictionary<UInt128, (DateTime Until, int Power)> Until =
            new Dictionary<UInt128, (DateTime, int)>();

        public UInt128 Status(DateTime now)
        {
            lock (this.Sync)
                return this.Until.Where(x => x.Value.Until > now).Aggregate(UInt128.Zero, (all, x) => all | x.Key);
        }

        public bool Has(UInt128 flag, DateTime now) => this.Power(flag, now) != null;

        public int? Power(UInt128 flag, DateTime now)
        {
            lock (this.Sync)
                return this.Until.TryGetValue(flag, out var status) && status.Until > now ? status.Power : null;
        }

        // True if the status wasn't on before
        public bool Set(UInt128 flag, DateTime until, DateTime now, int power = 0)
        {
            lock (this.Sync)
            {
                bool started = !this.Until.TryGetValue(flag, out var was) || was.Until <= now;
                this.Until[flag] = (until, power);
                return started;
            }
        }

        public void SetPower(UInt128 flag, int power)
        {
            lock (this.Sync)
                if (this.Until.TryGetValue(flag, out var status))
                    this.Until[flag] = (status.Until, power);
        }

        // Adds time to a status that's on, while less than the cap is left
        public bool Extend(UInt128 flag, TimeSpan by, TimeSpan cap, DateTime now)
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

        public bool Remove(UInt128 flag)
        {
            lock (this.Sync)
                return this.Until.Remove(flag);
        }

        // Returns the statuses it took away
        public UInt128 Expire(DateTime now)
        {
            lock (this.Sync)
            {
                UInt128 ended = 0;
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
