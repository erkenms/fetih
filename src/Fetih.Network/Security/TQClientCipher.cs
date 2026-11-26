namespace Fetih.Network.Security
{
    using System;

    // The client undoes the server's steps in reverse order, so its transform is the inverse.
    public sealed class TQClientCipher : ICipher
    {
        private ushort EncryptCounter;
        private ushort DecryptCounter;

        public void GenerateKeys(object[] seeds) =>
            throw new NotSupportedException("The client cipher isn't rekeyed");

        public void Decrypt(Span<byte> src, Span<byte> dst) =>
            Transform(src, dst, ref this.DecryptCounter);

        public void Encrypt(Span<byte> src, Span<byte> dst) =>
            Transform(src, dst, ref this.EncryptCounter);

        private static void Transform(Span<byte> src, Span<byte> dst, ref ushort counter)
        {
            var k = TQCipher.KInit;
            for (int i = 0; i < src.Length; i++)
            {
                byte value = (byte)(src[i] ^ k[counter & 0xff] ^ k[(counter >> 8) + 0x100]);
                value = (byte)(value >> 4 | value << 4);
                dst[i] = (byte)(value ^ 0xAB);
                counter++;
            }
        }
    }
}
