namespace Fetih.Account.Packets
{
    using Fetih.Account.States;
    using Fetih.Network.Packets;
    using Fetih.Network.Security;

    public sealed class MsgLoginChallengeS : MsgBase<Client>
    {
        // Each number has a length byte followed by a 128 byte slot
        public const int FieldSize = 128;
        public const int Size = 4 + 4 * (1 + MsgLoginChallengeS.FieldSize);

        public byte[] ServerPublicKey { get; set; }
        public byte[] Generator { get; set; }
        public byte[] Modulus { get; set; }
        public byte[] Salt { get; set; }

        public MsgLoginChallengeS(Srp6 srp)
        {
            this.ServerPublicKey = srp.ServerPublicKeyBytes;
            this.Generator = Srp6.ToBytes(Srp6.G);
            this.Modulus = Srp6.ToBytes(Srp6.N);
            this.Salt = srp.SaltBytes;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgLoginChallengeS);
            WriteField(writer, this.ServerPublicKey);   // 4
            WriteField(writer, this.Generator);         // 133
            WriteField(writer, this.Modulus);           // 262
            WriteField(writer, this.Salt);              // 391
            return writer.ToArray();
        }

        private static void WriteField(PacketWriter writer, byte[] value)
        {
            var slot = new byte[MsgLoginChallengeS.FieldSize];
            value.CopyTo(slot, 0);
            writer.Write((byte)value.Length);
            writer.Write(slot);
        }
    }
}
