namespace Comet.Account.Packets
{
    using Comet.Account.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1059</remarks>
    /// <summary>
    /// Message sent to the game client on connect containing a random seed. Patches 5174
    /// to 5531 used it for the RC5 password cipher; the SRP6 login doesn't use it.
    /// </summary>
    /// <remarks>
    /// 5619: from reference (still sent on connect; the SRP6 login doesn't use the seed,
    /// but the client still has a handler for it).
    /// </remarks>
    public sealed class MsgEncryptCode : MsgBase<Client>
    {
        public uint Seed { get; set; }

        /// <summary>
        /// Instantiates a new instance of <see cref="MsgEncryptCode"/> on client connect.
        /// </summary>
        /// <param name="seed">Random generated seed</param>
        public MsgEncryptCode(uint seed)
        {
            this.Seed = seed;
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
            writer.Write((ushort)PacketType.MsgEncryptCode);
            writer.Write(this.Seed);
            return writer.ToArray();
        }
    }
}
