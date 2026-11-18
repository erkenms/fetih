namespace Comet.Network.Security
{
    using System;

    /// <summary>
    /// The game client's side of <see cref="TQCipher"/>, for test clients and bots. The
    /// client undoes the server's steps in reverse order, so its transform is the
    /// inverse of the server's. Only the account server's use (no rekeying) is covered.
    /// </summary>
    public sealed class TQClientCipher : ICipher
    {
        // Counters for each direction
        private ushort EncryptCounter;
        private ushort DecryptCounter;

        /// <summary>The account server never rekeys this cipher.</summary>
        public void GenerateKeys(object[] seeds) =>
            throw new NotSupportedException("The client cipher isn't rekeyed");

        /// <summary>Decrypts bytes the server encrypted.</summary>
        public void Decrypt(Span<byte> src, Span<byte> dst) =>
            Transform(src, dst, ref this.DecryptCounter);

        /// <summary>Encrypts bytes for the server to decrypt.</summary>
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
