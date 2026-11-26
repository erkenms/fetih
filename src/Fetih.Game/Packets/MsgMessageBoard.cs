namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.Chat;
    using Fetih.Game.States;
    using Fetih.Network.Packets;
    using static Fetih.Game.Packets.MsgTalk;

    // Message board request and page list.
    // Layout: Length (0), Type 1111 (2), Index (4, ushort, 0-based page offset; client steps by 8),
    // Channel (6, ushort, TalkChannel 2201-2205), Action (8, byte), CNetStringPacker (9).
    // For Action=List (3), strings are triples: author name, preview text, timestamp as yyyyMMddHHmm.
    public sealed class MsgMessageBoard : MsgBase<Client>
    {
        public ushort Index { get; set; }
        public TalkChannel Channel { get; set; }
        public BoardAction Action { get; set; }
        public List<string> Strings { get; set; } = new List<string>();

        public MsgMessageBoard() => base.Type = PacketType.MsgMessageBoard;

        public MsgMessageBoard(ushort index, TalkChannel channel, BoardAction action, List<string> strings = null)
            : this()
        {
            this.Index = index;
            this.Channel = channel;
            this.Action = action;
            this.Strings = strings ?? new List<string>();
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Index = reader.ReadUInt16();
            this.Channel = (TalkChannel)reader.ReadUInt16();
            this.Action = (BoardAction)reader.ReadByte();
            this.Strings = bytes.Length > 9 ? reader.ReadStrings() : new List<string>();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Index);
            writer.Write((ushort)this.Channel);
            writer.Write((byte)this.Action);
            writer.Write(this.Strings ?? new List<string>());
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => MessageBoard.HandleAsync(client, this);

        public enum BoardAction : byte
        {
            Delete = 1,
            GetList = 2,
            List = 3,
            GetWords = 4,
        }
    }
}
