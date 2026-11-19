namespace Comet.Game.Packets
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Network.Packets;
    using Comet.Network.Security;
    using Org.BouncyCastle.Utilities.Encoders;

    public sealed class MsgHandshake : MsgBase<Client>
    {
        public byte[] DecryptionIV { get; private set; }
        public byte[] EncryptionIV { get; private set; }
        public string PrimeRoot { get; private set; }
        public string Generator { get; private set; }
        public string ServerKey { get; private set; }
        public string ClientKey { get; private set; }
        private byte[] Padding { get; set; }

        public MsgHandshake()
        {
        }

        // IVs are sent as zeros: the client's CAST5 always starts from a zero IV.
        public MsgHandshake(DiffieHellman dh)
        {
            this.PrimeRoot = Hex.ToHexString(dh.PrimeRoot.ToByteArrayUnsigned()).ToUpper();
            this.Generator = Hex.ToHexString(dh.Generator.ToByteArrayUnsigned()).ToUpper();
            this.ServerKey = Hex.ToHexString(dh.PublicKey.ToByteArrayUnsigned()).ToUpper();
            this.EncryptionIV = new byte[Cast5Cipher.BlockSize];
            this.DecryptionIV = new byte[Cast5Cipher.BlockSize];
        }

        public async Task RandomizeAsync()
        {
            this.Padding = new byte[23];
            await Kernel.NextBytesAsync(this.Padding);
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            reader.BaseStream.Seek(7, SeekOrigin.Begin);
            this.Length = (ushort)reader.ReadUInt32();
            reader.BaseStream.Seek(reader.ReadUInt32(), SeekOrigin.Current);
            this.ClientKey = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadInt32()));
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            var messageLength = 36 + this.Padding.Length + this.EncryptionIV.Length 
                + this.DecryptionIV.Length + this.PrimeRoot.Length + this.Generator.Length
                + this.ServerKey.Length;

            // Unframed, but PacketWriter's 2 reserved length bytes are harmless here.

            writer.Write(this.Padding.AsSpan(0, 9));
            writer.Write(messageLength - 11);
            writer.Write(this.Padding.Length - 11);
            writer.Write(this.Padding.AsSpan(9, this.Padding.Length - 11));
            writer.Write(this.EncryptionIV.Length);
            writer.Write(this.EncryptionIV);
            writer.Write(this.DecryptionIV.Length);
            writer.Write(this.DecryptionIV);
            writer.Write(this.PrimeRoot.Length);
            writer.Write(this.PrimeRoot, this.PrimeRoot.Length);
            writer.Write(this.Generator.Length);
            writer.Write(this.Generator, this.Generator.Length);
            writer.Write(this.ServerKey.Length);
            writer.Write(this.ServerKey, this.ServerKey.Length);
            return writer.ToArray();
        }
    }
}