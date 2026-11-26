namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.Chat;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Broadcast queue request/reply.
    // Layout: Length (0), Type 2050 (2), Action (4, byte), Currency (5, byte; 1 CPs, 2 bound CPs),
    // Padding (6, ushort), Param (8, uint: 0-based page for queries, entry ID for urgent,
    // 1-based queue rank for server replies), CNetStringPacker (12; 3 zero bytes when empty,
    // matching the 5619 client's 15-byte minimum size).
    public sealed class MsgPigeon : MsgBase<Client>
    {
        public PigeonAction Action { get; set; }
        public byte Currency { get; set; }
        public uint Param { get; set; }
        public List<string> Strings { get; set; } = new List<string>();

        public MsgPigeon() => base.Type = PacketType.MsgPigeon;

        public MsgPigeon(PigeonAction action, uint param = 0) : this()
        {
            this.Action = action;
            this.Param = param;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (PigeonAction)reader.ReadByte();
            this.Currency = reader.ReadByte();
            reader.ReadUInt16();
            this.Param = reader.ReadUInt32();
            this.Strings = bytes.Length > 12 ? reader.ReadStrings() : new List<string>();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((byte)this.Action);
            writer.Write(this.Currency);
            writer.Write((ushort)0);
            writer.Write(this.Param);
            if (this.Strings != null && this.Strings.Count > 0)
                writer.Write(this.Strings);
            else
                writer.Write(new byte[3]);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Broadcasts.HandleAsync(client, this);

        public enum PigeonAction : byte
        {
            QueryAll = 1,
            QueryMine = 2,
            Send = 3,
            UrgentTop = 4,
            Urgent = 5,
            Remove = 6,
        }
    }
}
