namespace Fetih.Game.Packets
{
    using System.Threading.Tasks;
    using Fetih.Game.Social;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // The action at 4, a player ID at 8
    public sealed class MsgTeam : MsgBase<Client>
    {
        public TeamAction Action { get; set; }
        public uint Data { get; set; }

        public MsgTeam() => base.Type = PacketType.MsgTeam;

        public MsgTeam(TeamAction action, uint data) : this()
        {
            this.Action = action;
            this.Data = data;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (TeamAction)reader.ReadUInt32();
            this.Data = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((uint)this.Action);        // 4
            writer.Write(this.Data);                // 8
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Teams.HandleAsync(client, this);

        public enum TeamAction : uint
        {
            Create = 0,
            // To a team member: the asker joins if their leader agrees
            ApplyJoin = 1,
            Leave = 2,
            AcceptInvite = 3,
            Invite = 4,
            AcceptJoin = 5,
            Dismiss = 6,
            Kick = 7,
            // Sent before the Create reply
            Leader = 15,
        }
    }
}
