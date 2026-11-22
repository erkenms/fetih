namespace Comet.Game.World
{
    using System;
    using System.Threading;

    // A monster from a generator. Health and life change under Lock, since players hit it from
    // their own packet queues and guards from the monster loop. Position, direction and the
    // timers are changed by the monster loop only.
    public sealed class Monster : IMapObject
    {
        public const uint FirstID = 400001;
        public const uint LastID = 499999;

        public uint Identity { get; }
        public MonsterType Type { get; }
        public Generator Generator { get; }
        public GameMap<Monster> Map { get; }

        public readonly object Lock = new object();
        private uint health;
        private bool alive;
        private uint target;

        public ushort X { get; set; }
        public ushort Y { get; set; }
        public byte Direction { get; set; }
        public DateTime DiedAt { get; set; }
        public bool CorpseRemoved { get; set; }
        public DateTime NextAttackAt { get; set; }
        public DateTime NextMoveAt { get; set; }

        public Monster(uint identity, MonsterType type, Generator generator, GameMap<Monster> map)
        {
            this.Identity = identity;
            this.Type = type;
            this.Generator = generator;
            this.Map = map;
        }

        public uint Health { get { lock (this.Lock) return this.health; } }
        public bool Alive { get { lock (this.Lock) return this.alive; } }
        public bool IsGuard => this.Type.IsGuard;

        // The role it chases and attacks: a player's character ID or a monster's ID; 0 for none
        public uint Target
        {
            get => Volatile.Read(ref this.target);
            set => Volatile.Write(ref this.target, value);
        }

        public void Revive()
        {
            lock (this.Lock)
            {
                this.health = this.Type.Life;
                this.alive = true;
                this.CorpseRemoved = false;
                this.Target = 0;
                this.poison = null;
                this.stunned = false;
            }
        }

        private Magic.Poisoning poison;
        private DateTime stunnedUntil;
        private bool stunned;

        // Can't attack until then; false if it's dead
        public bool Stun(DateTime until)
        {
            lock (this.Lock)
            {
                if (!this.alive)
                    return false;
                this.stunnedUntil = until;
                this.stunned = true;
                return true;
            }
        }

        public bool Stunned(DateTime now)
        {
            lock (this.Lock)
                return this.stunned && now < this.stunnedUntil;
        }

        // True once, when a stun has run out
        public bool EndStun(DateTime now)
        {
            lock (this.Lock)
            {
                if (!this.stunned || now < this.stunnedUntil)
                    return false;
                this.stunned = false;
                return true;
            }
        }

        // False if it's dead
        public bool Poison(Magic.Poisoning poison)
        {
            lock (this.Lock)
            {
                if (!this.alive)
                    return false;
                this.poison = poison;
                return true;
            }
        }

        // What a poison due took, the health left, and whether it's over; null if none was due
        public (uint Taken, uint Health, bool Ended)? TickPoison(DateTime now)
        {
            lock (this.Lock)
            {
                if (this.poison == null || !this.alive || now < this.poison.NextAt)
                    return null;
                uint taken = this.poison.Take(this.health);
                this.health -= taken;
                this.poison.Left--;
                this.poison.NextAt += this.poison.Interval;
                bool ended = this.poison.Left <= 0;
                if (ended)
                    this.poison = null;
                return (taken, this.health, ended);
            }
        }

        // An attack that missed still makes the attacker its target, if it has none
        public void Provoke(uint attackerID)
        {
            lock (this.Lock)
                if (this.alive && this.target == 0)
                    this.target = attackerID;
        }

        // Only the hit that empties its health reports the kill, so of many attackers at once
        // exactly one kills it. The first attacker becomes its target.
        public (uint Dealt, bool Killed) Damage(uint amount, uint attackerID, DateTime now)
        {
            lock (this.Lock)
            {
                if (!this.alive)
                    return (0, false);
                uint dealt = Math.Min(this.health, amount);
                this.health -= dealt;
                if (attackerID != 0 && this.target == 0)
                    this.target = attackerID;
                if (this.health > 0)
                    return (dealt, false);
                this.alive = false;
                this.DiedAt = now;
                this.target = 0;
                return (dealt, true);
            }
        }
    }
}
