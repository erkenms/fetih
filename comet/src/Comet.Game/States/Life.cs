namespace Comet.Game.States
{
    using System;

    // Whether a player is alive, kept in memory only: logging out dead revives the character.
    // Changed on the player's own packet queue; the monster loop only reads Dead.
    public sealed class Life
    {
        // Status bit (MsgUserAttrib 25, MsgPlayer 22) for the dead: the client shows its revive
        // buttons. Albetros also sets 0x20, which the wiki doesn't list. The ghost is only the body.
        public const ulong DeadStatus = StatusFlags.Dead;

        // The ghost look replaces the body with a transformation: 98 for men, 99 for women
        private const uint TransformFactor = 10_000_000;

        private volatile bool dead;
        public bool Dead { get => this.dead; set => this.dead = value; }
        public DateTime DiedAt { get; set; }
        public bool Ghost { get; set; }
        public ulong Status => this.Dead ? DeadStatus : 0;

        // Starts after the player last revived; monsters can't hurt them until then
        public DateTime ProtectedUntil { get; set; }

        public uint Mesh(Character character)
        {
            uint mesh = (uint)(character.Mesh + character.Avatar * 10000);
            if (!this.Ghost)
                return mesh;
            uint ghost = character.Mesh % 10 < 3 ? 99u : 98u;
            return ghost * TransformFactor + mesh;
        }
    }
}
