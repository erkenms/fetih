namespace Fetih.Game.Packets
{
    using System;
    using System.Threading.Tasks;
    using Fetih.Game.Social;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Clan War map dialog (CDlgFamilyOccupy), always 136 bytes. Action (u16, u32 wide) at 4.
    // From the client a war map's banner NPC is at 8, except the query (6), which has it at 12.
    // The query's answer: NPC at 8 and 12, the asker's role at 16 (0 a member and 1 the leader
    // of the occupying clan, 3 a leader signed up here, 5 a leader who may sign up), the
    // occupying clan's name at 20 and the map's name at 56 (36 bytes each), then bools: the
    // Join button at 92, the Apply button at 93, Give Up and Cancel allowed (no war on) at 94;
    // days occupied at 96, the daily and the next weekly reward pack at 100 and 104, other
    // clans signed up at 112, the fee (u64) at 120, revenue unclaimed at 128. Actions 11 and
    // 19 carry a player at 132, who gets the reward effect.
    public sealed class MsgFamilyOccupy : MsgBase<Client>
    {
        public const int Size = 136;

        public OccupyAction Action { get; set; }
        public uint NpcID { get; set; }
        public uint Role { get; set; }
        public string Occupier { get; set; } = string.Empty;
        public string MapName { get; set; } = string.Empty;
        public bool CanJoin { get; set; }
        public bool CanApply { get; set; }
        public bool CanChange { get; set; }
        public uint Days { get; set; }
        public uint DailyPrize { get; set; }
        public uint WeeklyPrize { get; set; }
        public bool Challenged { get; set; }
        public ulong Fee { get; set; }
        public bool RevenueUnclaimed { get; set; }
        public uint PlayerID { get; set; }

        public MsgFamilyOccupy() => base.Type = PacketType.MsgFamilyOccupy;

        public MsgFamilyOccupy(OccupyAction action, uint npcID = 0) : this()
        {
            this.Action = action;
            this.NpcID = npcID;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (OccupyAction)reader.ReadUInt32();
            uint at8 = reader.ReadUInt32();
            uint at12 = reader.ReadUInt32();
            this.NpcID = this.Action == OccupyAction.Query ? at12 : at8;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((uint)this.Action);            // 4
            writer.Write(this.NpcID);                   // 8
            writer.Write(this.NpcID);                   // 12
            writer.Write(this.Role);                    // 16
            writer.Write(Fit(this.Occupier), 36);       // 20
            writer.Write(Fit(this.MapName), 36);        // 56
            writer.Write(this.CanJoin);                 // 92
            writer.Write(this.CanApply);                // 93
            writer.Write(this.CanChange);               // 94
            writer.Write((byte)0);
            writer.Write(this.Days);                    // 96
            writer.Write(this.DailyPrize);              // 100
            writer.Write(this.WeeklyPrize);             // 104
            writer.Write(0u);                           // 108
            writer.Write(this.Challenged);              // 112
            writer.Write(new byte[7]);
            writer.Write(this.Fee);                     // 120
            writer.Write(this.RevenueUnclaimed);        // 128
            writer.Write(new byte[3]);
            writer.Write(this.PlayerID);                // 132
            return writer.ToArray();
        }

        private static string Fit(string text) =>
            text == null ? string.Empty : text.Length > 35 ? text[..35] : text;

        public override Task ProcessAsync(Client client) => ClanWar.HandleAsync(client, this);

        public enum OccupyAction : uint
        {
            // From the leader: sign up for this map's war, after the fee's confirmation
            Apply = 0,
            // From the leader: withdraw the sign-up (the fee is kept)
            Cancel = 1,
            // From the occupying leader: give the map up
            GiveUp = 2,
            // To the client: "You have abandoned" the dialog's map
            Abandoned = 3,
            // To the client: the clan is signed up elsewhere; confirming sends ApplyAgain
            AskReapply = 4,
            // Asks for the dialog (banner clicked); answered with the same action
            Query = 6,
            // To members when the war starts: the map's NPC and name; accepting sends Join
            Invite = 7,
            Join = 8,
            ApplyAgain = 10,
            // To the client: tips shown in a message box (STR_FAMILY_OCCUPY_TEXT25 and 42)
            OnceADay = 13,
            // From the occupying leader: claim today's revenue
            ClaimRevenue = 18,
            RevenueClaimed = 19,
            NeedSpace = 20,
        }
    }
}
