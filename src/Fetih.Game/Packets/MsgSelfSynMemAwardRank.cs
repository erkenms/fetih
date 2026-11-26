namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // The guild reward pools (CMsgSelfSynMemAwardRank, CSelfSynMemAwardRankMgr), 92 bytes from the
    // client: action u16 at 4, event u32 at 6 (1 Capture the Flag, 2 Guild Contest/PK, 3 the
    // cross-server war), page u32 at 10 (1-based) or from the server the entries in all pages,
    // count u32 at 14, CPs u32 at 18, silver u64 at 22, entries from 30.
    // From the client: 0 asks for the guilds' pools, 1 for the guild's last split, 8 for the
    // Capture the Flag exploits, 9 for the last eight; 2-7 the leader adding to the pool, 2 and 5
    // both currencies, 3 and 6 CPs, 4 and 7 silver.
    // From the server the same actions answer: 0 entries of 52 bytes (CPs u32, silver u64, guild
    // name at 12), 1 entries of 60 (exploits u32 at 4, CPs u32 at 8, silver u64 at 12, character
    // u32 at 20, name at 24), 8 entries of 20 (name 16, exploits u32) with the reader's exploits
    // at 18; 2-7 the pool's new totals; 13 with event 1 the reader's exploits at 18. CPs and
    // silver in 0, 1 and 2-7 are the guild's pool for its next match.
    public sealed class MsgSelfSynMemAwardRank : MsgBase<Client>
    {
        public enum AwardAction : ushort
        {
            GuildPools = 0,
            MemberShares = 1,
            AddBoth = 2,
            AddConquerPoints = 3,
            AddSilver = 4,
            AddBothAgain = 5,
            AddConquerPointsAgain = 6,
            AddSilverAgain = 7,
            Exploits = 8,
            LastEight = 9,
            OwnExploits = 13,
        }

        public enum AwardEvent : uint { CaptureTheFlag = 1, GuildPk = 2, CrossServer = 3 }

        public const int MinimumLength = 92;

        public AwardAction Action { get; set; }
        public AwardEvent Event { get; set; }
        public uint Page { get; set; }
        public uint Total { get; set; }
        public uint ConquerPoints { get; set; }
        public ulong Silver { get; set; }
        public List<(uint ConquerPoints, ulong Silver, string Name)> Pools { get; } = new List<(uint, ulong, string)>();
        public List<(uint Exploits, uint ConquerPoints, ulong Silver, uint CharacterID, string Name)> Shares { get; } =
            new List<(uint, uint, ulong, uint, string)>();
        public List<(string Name, uint Exploits)> Exploits { get; } = new List<(string, uint)>();

        public MsgSelfSynMemAwardRank() => base.Type = PacketType.MsgSelfSynMemAwardRank;

        public MsgSelfSynMemAwardRank(AwardAction action, AwardEvent ev) : this()
        {
            this.Action = action;
            this.Event = ev;
        }

        public bool IsAdd => this.Action >= AwardAction.AddBoth && this.Action <= AwardAction.AddSilverAgain;

        // What an add asks for: 3/6 carry only CPs and 4/7 only silver
        public (uint ConquerPoints, ulong Silver) Added => this.Action switch
        {
            AwardAction.AddConquerPoints or AwardAction.AddConquerPointsAgain => (this.ConquerPoints, 0),
            AwardAction.AddSilver or AwardAction.AddSilverAgain => (0, this.Silver),
            _ => (this.ConquerPoints, this.Silver),
        };

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (AwardAction)reader.ReadUInt16(); // 4
            this.Event = (AwardEvent)reader.ReadUInt32();   // 6
            this.Page = reader.ReadUInt32();                // 10
            this.Total = reader.ReadUInt32();               // 14
            this.ConquerPoints = reader.ReadUInt32();       // 18
            this.Silver = reader.ReadUInt64();              // 22
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)this.Action);              // 4
            writer.Write((uint)this.Event);                 // 6
            writer.Write(this.Total);                       // 10
            int count = this.Pools.Count + this.Shares.Count + this.Exploits.Count;
            writer.Write((uint)count);                      // 14
            writer.Write(this.ConquerPoints);               // 18
            writer.Write(this.Silver);                      // 22
            int size = 30;
            foreach (var (cps, silver, name) in this.Pools) // 30
            {
                writer.Write(cps);
                writer.Write(silver);
                writer.Write(Fit(name), 40);
                size += 52;
            }
            foreach (var (exploits, cps, silver, id, name) in this.Shares)
            {
                writer.Write(0u);
                writer.Write(exploits);
                writer.Write(cps);
                writer.Write(silver);
                writer.Write(id);
                writer.Write(Fit(name), 36);
                size += 60;
            }
            foreach (var (name, exploits) in this.Exploits)
            {
                writer.Write(Fit(name), 16);
                writer.Write(exploits);
                size += 20;
            }
            if (size < MinimumLength)
                writer.Write(new byte[MinimumLength - size]);
            return writer.ToArray();
        }

        private static string Fit(string name) =>
            string.IsNullOrEmpty(name) ? string.Empty : name.Length > 15 ? name.Substring(0, 15) : name;

        public override Task ProcessAsync(Client client) => Social.GuildRewards.HandleAsync(client, this);
    }
}
