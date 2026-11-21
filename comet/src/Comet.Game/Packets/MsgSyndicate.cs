namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Social;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Action at 4, a player, guild or amount at 8, the join
    // requirements (level, rebirths, professions) at 12, 16 and 20, strings at 24
    public sealed class MsgSyndicate : MsgBase<Client>
    {
        public SyndicateAction Action { get; set; }
        public uint Data { get; set; }
        public uint RequiredLevel { get; set; }
        public uint RequiredRebirths { get; set; }
        public uint RequiredProfessions { get; set; }
        public List<string> Strings { get; set; } = new List<string>();

        public MsgSyndicate() => base.Type = PacketType.MsgSyndicate;

        public MsgSyndicate(SyndicateAction action, uint data, params string[] strings) : this()
        {
            this.Action = action;
            this.Data = data;
            this.Strings = new List<string>(strings);
        }

        public string Text => this.Strings.Count > 0 ? this.Strings[0] : string.Empty;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (SyndicateAction)reader.ReadUInt32();
            this.Data = reader.ReadUInt32();
            if (bytes.Length < 24)
                return;
            this.RequiredLevel = reader.ReadUInt32();
            this.RequiredRebirths = reader.ReadUInt32();
            this.RequiredProfessions = reader.ReadUInt32();
            if (bytes.Length > 24)
                this.Strings = reader.ReadStrings();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((uint)this.Action);        // 4
            writer.Write(this.Data);                // 8
            writer.Write(this.RequiredLevel);       // 12
            writer.Write(this.RequiredRebirths);    // 16
            writer.Write(this.RequiredProfessions); // 20
            writer.Write(this.Strings);             // 24
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Guilds.HandleAsync(client, this);

        public enum SyndicateAction : uint
        {
            // From the client with a guild member's ID; forwarded to them with the asker's
            ApplyJoin = 1,
            // From the client with the invited player's ID; forwarded with the inviter's
            InviteJoin = 2,
            Leave = 3,
            // Asks for a guild's name by its ID; answered with MsgName Guild
            QueryName = 6,
            Ally = 7,
            Unally = 8,
            Enemy = 9,
            Unenemy = 10,
            DonateSilver = 11,
            QueryAttribute = 12,
            // Tells the client which guild the player is in
            SetSyndicate = 14,
            Disband = 19,
            DonateConquerPoints = 20,
            // To the other leader with the asking guild's ID and name
            AllyRequest = 23,
            SetRequirements = 24,
            Announce = 27,
            Promote = 28,
            Discharge = 30,
            PromotionList = 37,
        }
    }
}
