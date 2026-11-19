namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 10005</remarks>
    /// <summary>
    /// Message for one step: the client sends it for each cell it walks or runs, and the
    /// server passes it on to the players who can see the walker (and back to the walker).
    /// </summary>
    public sealed class MsgWalk : MsgBase<Client>
    {
        public uint Direction { get; set; }
        public uint CharacterID { get; set; }
        public uint Mode { get; set; }
        public uint Timestamp { get; set; }
        public uint Map { get; set; }

        /// <summary>
        /// Decodes a byte packet into the packet structure defined by this message class. 
        /// Should be invoked to structure data from the client for processing. Decoding
        /// follows TQ Digital's byte ordering rules for an all-binary protocol.
        /// </summary>
        /// <remarks>
        /// 5619: seen in VM (24 bytes: direction at 4, which the client sends as any
        /// number whose remainder by 8 is the direction; character ID at 8; mode at 12,
        /// 1 when walking; timestamp at 16; map at 20, sent as 0) and the reference
        /// (field meanings).
        /// </remarks>
        /// <param name="bytes">Bytes from the packet processor or client socket</param>
        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Direction = reader.ReadUInt32();
            this.CharacterID = reader.ReadUInt32();
            this.Mode = reader.ReadUInt32();
            this.Timestamp = reader.ReadUInt32();
            this.Map = reader.ReadUInt32();
        }

        /// <summary>
        /// Encodes the packet structure defined by this message class into a byte packet
        /// that can be sent to the client. Invoked automatically by the client's send 
        /// method. Encodes using byte ordering rules interoperable with the game client.
        /// </summary>
        /// <returns>Returns a byte packet of the encoded packet.</returns>
        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgWalk);
            writer.Write(this.Direction);
            writer.Write(this.CharacterID);
            writer.Write(this.Mode);
            writer.Write(this.Timestamp);
            writer.Write(this.Map);
            return writer.ToArray();
        }

        /// <summary>The direction, 0 to 7.</summary>
        public byte Facing => (byte)(this.Direction % 8);

        /// <summary>Moves the walker one cell, or puts them back if they can't go there.</summary>
        /// <param name="client">Client requesting packet processing</param>
        public override Task ProcessAsync(Client client) => Movement.WalkAsync(client, this);
    }
}
