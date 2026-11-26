namespace Fetih.Game.Packets
{
    using System.Threading.Tasks;
    using Fetih.Game.States;
    using Fetih.Game.Training;
    using Fetih.Network.Packets;

    // Heaven blessing EXP waiting to be claimed (5619 client's CMsgGodExp), 16 bytes:
    // action byte at 4, online training EXP u32 at 8, blessed hunting EXP u32 at 12. The
    // client shows them as value / 10000 / 600 EXP balls. The server answers Query with the
    // amounts, and sends 3 and 4 to ask the player to claim a full online or hunting pool.
    public sealed class MsgGodExp : MsgBase<Client>
    {
        public GodExpAction Action { get; set; }
        public uint Online { get; set; }
        public uint Hunting { get; set; }

        public MsgGodExp() => base.Type = PacketType.MsgGodExp;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (GodExpAction)reader.ReadByte();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((byte)this.Action);    // 4
            writer.Write(new byte[3]);
            writer.Write(this.Online);          // 8
            writer.Write(this.Hunting);         // 12
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => GodExp.HandleAsync(client, this.Action);

        public enum GodExpAction : byte
        {
            Query = 0,
            ClaimOnline = 1,
            ClaimHunting = 2,
            OnlineFull = 3,
            HuntingFull = 4,
        }
    }
}
