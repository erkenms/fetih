namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Social;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Action at 4, a player, clan or amount at 8, then strings at 16. The member list has
    // a count at 16 and 36-byte entries: name (16 bytes), level at 16, rank (u16) at 20,
    // online (u16) at 22, class at 24, look at 28, donation at 32. The ally and enemy lists
    // have a count at 16 and 56-byte entries: clan ID, name (16 bytes) at 4, leader name
    // (16 bytes) at 40.
    public sealed class MsgFamily : MsgBase<Client>
    {
        public const int MemberSize = 36;
        public const int RelationSize = 56;

        public FamilyAction Action { get; set; }
        public uint Data { get; set; }
        public List<string> Strings { get; set; } = new List<string>();
        public List<(ClanMember Member, bool Online)> Members { get; set; }
        public List<Clan> Relations { get; set; }

        public MsgFamily() => base.Type = PacketType.MsgFamily;

        public MsgFamily(FamilyAction action, uint data, params string[] strings) : this()
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
            this.Action = (FamilyAction)reader.ReadUInt32();
            this.Data = reader.ReadUInt32();
            if (bytes.Length <= 16)
                return;
            reader.ReadUInt32();
            this.Strings = reader.ReadStrings();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((uint)this.Action);            // 4
            writer.Write(this.Data);                    // 8
            writer.Write(0u);                           // 12
            if (this.Members != null)
            {
                writer.Write(this.Members.Count);       // 16
                foreach (var (member, online) in this.Members)
                {
                    writer.Write(member.Name, 16);
                    writer.Write((uint)member.Level);
                    writer.Write((ushort)member.Rank);
                    writer.Write((ushort)(online ? 1 : 0));
                    writer.Write((uint)member.Class);
                    writer.Write(member.Mesh);
                    writer.Write((uint)System.Math.Min(member.Donation, uint.MaxValue));
                }
            }
            else if (this.Relations != null)
            {
                writer.Write(this.Relations.Count);     // 16
                foreach (var clan in this.Relations)
                {
                    writer.Write(clan.ID);
                    writer.Write(clan.Name, 16);
                    writer.Write(new byte[20]);
                    writer.Write(clan.Leader?.Name ?? string.Empty, 16);
                }
            }
            else
                writer.Write(this.Strings);             // 16
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Clans.HandleAsync(client, this);

        public enum FamilyAction : uint
        {
            // Asks for the clan window; answered with the clan's details as strings
            Query = 1,
            QueryMembers = 4,
            // From the leader with a player's ID; forwarded with the leader's ID, clan and name
            Recruit = 9,
            AcceptRecruit = 10,
            // From a player with a clan leader's ID; forwarded with the asker's ID and name
            Join = 11,
            AcceptJoin = 12,
            EnemyList = 13,
            AddEnemy = 14,
            DeleteEnemy = 15,
            AllyList = 16,
            // From the leader with the other leader's ID; forwarded with the clan, its name
            // and leader
            AddAlly = 17,
            AcceptAlly = 18,
            DeleteAlly = 20,
            Abdicate = 21,
            Kick = 22,
            Quit = 23,
            Announce = 24,
            SetAnnouncement = 25,
            Donate = 26,
            QueryOccupy = 29,
        }
    }
}
