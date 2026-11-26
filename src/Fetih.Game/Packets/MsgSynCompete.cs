namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // The Guild Contest's scores (CMsgSynCompete), drawn on maps flagged GuildContest: action
    // u16 at 4; 0 replaces the ranking with count u32 at 8 entries of 48 bytes from 16 (rank
    // u32, points u64, name 36 bytes); 1 sets the player's guild points from the first entry's
    // points (u64 at 20); 2 sets the player's Exploits (u32 at 12).
    public sealed class MsgSynCompete : MsgBase<Client>
    {
        public enum CompeteAction : ushort { Ranking = 0, GuildPoints = 1, Exploits = 2 }

        public CompeteAction Action { get; set; }
        public uint Exploits { get; set; }
        public List<(uint Rank, long Points, string Name)> Entries { get; } = new List<(uint, long, string)>();

        public MsgSynCompete() => base.Type = PacketType.MsgSynCompete;

        public static MsgSynCompete GuildPoints(long points) =>
            new MsgSynCompete { Action = CompeteAction.GuildPoints, Entries = { (0, points, string.Empty) } };

        public static MsgSynCompete OfExploits(uint exploits) =>
            new MsgSynCompete { Action = CompeteAction.Exploits, Exploits = exploits };

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)this.Action);          // 4
            writer.Write((ushort)0);                    // 6
            writer.Write((uint)this.Entries.Count);     // 8
            writer.Write(this.Exploits);                // 12
            foreach (var (rank, points, name) in this.Entries)
            {
                writer.Write(rank);                     // 16 + 48n
                writer.Write(points);
                writer.Write(name ?? string.Empty, 36);
            }
            if (this.Entries.Count == 0)
                writer.Write(new byte[48]);
            return writer.ToArray();
        }
    }
}
