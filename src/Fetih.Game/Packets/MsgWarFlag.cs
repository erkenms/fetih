namespace Fetih.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Capture the Flag (CMsgWarFlag, CWarFlagMgr), at least 56 bytes: action u16 at 4, data u32
    // at 8. When u32 at 12 (a guild) isn't 0, the client keeps the u16 X/Y pairs at 16, 20 and
    // 24 as that guild's bases for the radar (pairs of 0 skipped). A count u16 at 28 and entries
    // from 32 follow: ranks of 24 bytes (index u32, score u32, name 16) or X/Y pairs of 4.
    // From the client: 6 touched the flag NPC in data (a role 47 NPC next to the hero), 7 walked
    // into a flag base (ini/region.ini type 25), 11 asks whether the event runs.
    public sealed class MsgWarFlag : MsgBase<Client>
    {
        public enum FlagAction : ushort
        {
            Enter = 0,              // shows the war's windows
            BaseDamageRank = 1,     // read only while the hero stands in a base
            ScoreRank = 2,
            FlagPositions = 3,      // the flags on the radar
            OccupyInfo = 5,
            PickedUp = 6,           // the carrier's effect; from the client, touching a flag
            Planted = 7,            // the planter's effect and message; from the client, reaching a base
            Countdown = 8,          // seconds left to plant
            Leave = 9,              // hides the war's windows
            GameOver = 10,
            Running = 11,           // data bit 0
            DoubleScore = 12,       // the X2 base: NPC in data, one X/Y entry
        }

        public FlagAction Action { get; set; }
        public uint Data { get; set; }
        public uint GuildID { get; set; }
        public List<(ushort X, ushort Y)> GuildBases { get; } = new List<(ushort, ushort)>();
        public List<(uint Score, string Name)> Ranks { get; } = new List<(uint, string)>();
        public List<(ushort X, ushort Y)> Positions { get; } = new List<(ushort, ushort)>();

        public MsgWarFlag() => base.Type = PacketType.MsgWarFlag;

        public MsgWarFlag(FlagAction action, uint data = 0) : this()
        {
            this.Action = action;
            this.Data = data;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (FlagAction)reader.ReadUInt16();
            reader.ReadUInt16();
            this.Data = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)this.Action);          // 4
            writer.Write((ushort)0);
            writer.Write(this.Data);                    // 8
            writer.Write(this.GuildID);                 // 12
            for (int i = 0; i < 3; i++)                 // 16
            {
                var (x, y) = i < this.GuildBases.Count ? this.GuildBases[i] : ((ushort)0, (ushort)0);
                writer.Write(x);
                writer.Write(y);
            }
            int count = this.Ranks.Count > 0 ? this.Ranks.Count : this.Positions.Count;
            writer.Write((ushort)count);                // 28
            writer.Write((ushort)0);
            int size = 32;
            for (int i = 0; i < this.Ranks.Count; i++)  // 32
            {
                writer.Write((uint)i);
                writer.Write(this.Ranks[i].Score);
                writer.Write(Fit(this.Ranks[i].Name), 16);
                size += 24;
            }
            if (this.Ranks.Count == 0)
                foreach (var (x, y) in this.Positions)
                {
                    writer.Write(x);
                    writer.Write(y);
                    size += 4;
                }
            if (size < 56)
                writer.Write(new byte[56 - size]);
            return writer.ToArray();
        }

        private static string Fit(string name) =>
            string.IsNullOrEmpty(name) ? string.Empty : name.Length > 15 ? name.Substring(0, 15) : name;

        public override Task ProcessAsync(Client client) => Social.CaptureTheFlag.HandleAsync(client, this);
    }
}
