namespace Fetih.Game.Pk
{
    using System;
    using System.Threading;
    using Fetih.Game.States;

    // As the client's PK mode button cycles them (MsgAction 96)
    public enum PkMode : uint
    {
        Free = 0,
        Peace = 1,
        Team = 2,
        Capture = 3,
    }

    // A player's PK mode and flashing, kept in memory only, and the decay of their PK points.
    // Kills change them from the killer's packet queue and the PK loop from its own, so PK
    // points and flashing only change under the lock.
    public sealed class PkState
    {
        private readonly object Sync = new object();
        private int mode = (int)PkMode.Capture;
        private DateTime FlashingUntil;
        private DateTime NextDecayAt;

        public PkMode Mode
        {
            get => (PkMode)Volatile.Read(ref this.mode);
            set => Volatile.Write(ref this.mode, (int)value);
        }

        public bool IsFlashing(DateTime now)
        {
            lock (this.Sync)
                return this.FlashingUntil > now;
        }

        // True if the player wasn't flashing before
        public bool Flash(DateTime now, TimeSpan duration)
        {
            lock (this.Sync)
            {
                bool started = this.FlashingUntil <= now;
                if (now + duration > this.FlashingUntil)
                    this.FlashingUntil = now + duration;
                return started;
            }
        }

        // True once, when flashing has run out
        public bool StopFlashing(DateTime now)
        {
            lock (this.Sync)
            {
                if (this.FlashingUntil == default || this.FlashingUntil > now)
                    return false;
                this.FlashingUntil = default;
                return true;
            }
        }

        public bool ClearFlashing(DateTime now)
        {
            lock (this.Sync)
            {
                bool was = this.FlashingUntil > now;
                this.FlashingUntil = default;
                return was;
            }
        }

        public ulong Status(Character character, DateTime now)
        {
            lock (this.Sync)
                return Pking.NameStatus(character.KillPoints) |
                    (this.FlashingUntil > now ? Pking.FlashingStatus : 0);
        }

        public (ushort From, ushort To) ChangePoints(Character character, int amount)
        {
            lock (this.Sync)
            {
                ushort from = character.KillPoints;
                character.KillPoints = (ushort)Math.Clamp(from + amount, 0, ushort.MaxValue);
                return (from, character.KillPoints);
            }
        }

        // Takes one point each interval online; the first call starts the clock
        public bool Decay(Character character, DateTime now, TimeSpan interval, out ushort points)
        {
            lock (this.Sync)
            {
                points = character.KillPoints;
                if (this.NextDecayAt == default)
                {
                    this.NextDecayAt = now + interval;
                    return false;
                }
                if (now < this.NextDecayAt)
                    return false;
                this.NextDecayAt = now + interval;
                if (points == 0)
                    return false;
                points = --character.KillPoints;
                return true;
            }
        }
    }
}
