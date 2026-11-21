namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.Items;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 20 bytes: the player's ID at 4, the item at 8, the gem at 12, the socket (1 or 2) as a
    // ushort at 16 and the action as a ushort at 18. Echoed once done.
    public sealed class MsgGemEmbed : MsgBase<Client>
    {
        public uint CharacterID { get; set; }
        public uint ItemID { get; set; }
        public uint GemID { get; set; }
        public ushort Socket { get; set; }
        public EmbedAction Action { get; set; }

        public MsgGemEmbed() => base.Type = PacketType.MsgGemEmbed;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.CharacterID = reader.ReadUInt32();
            this.ItemID = reader.ReadUInt32();
            this.GemID = reader.ReadUInt32();
            this.Socket = reader.ReadUInt16();
            this.Action = (EmbedAction)reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.CharacterID);         // 4
            writer.Write(this.ItemID);              // 8
            writer.Write(this.GemID);               // 12
            writer.Write(this.Socket);              // 16
            writer.Write((ushort)this.Action);      // 18
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Embedding.HandleAsync(client, this);

        public enum EmbedAction : ushort
        {
            Embed = 0,
            Remove = 1,
        }
    }
}
