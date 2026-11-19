namespace Comet.Account.Packets
{
    using Comet.Account.States;
    using Comet.Network.Packets;

    // The SRP6 login doesn't use the seed; sent because the client still handles it.
    public sealed class MsgEncryptCode : MsgBase<Client>
    {
        public uint Seed { get; set; }

        public MsgEncryptCode(uint seed)
        {
            this.Seed = seed;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgEncryptCode);
            writer.Write(this.Seed);
            return writer.ToArray();
        }
    }
}
