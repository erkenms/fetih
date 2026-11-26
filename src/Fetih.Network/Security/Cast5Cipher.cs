namespace Fetih.Network.Security
{
    using System;
    using System.Text;
    using Org.BouncyCastle.Crypto.Engines;
    using Org.BouncyCastle.Crypto.Parameters;

    // Matches the client's OpenSSL CAST5 CFB64: keys cut to 16 bytes, both IVs start at zero
    // and reset when keys are regenerated.
    public sealed class Cast5Cipher : ICipher
    {
        public const int BlockSize = 8;
        public const int MaxKeySize = 16;
        public const string DefaultSeed = "BC234xs45nme7HU9";

        private readonly Cast5Engine Engine;
        private readonly byte[] DecryptionIV, EncryptionIV;
        private int DecryptCount, EncryptCount;

        public Cast5Cipher(string seed = Cast5Cipher.DefaultSeed)
        {
            this.Engine = new Cast5Engine();
            this.DecryptionIV = new byte[Cast5Cipher.BlockSize];
            this.EncryptionIV = new byte[Cast5Cipher.BlockSize];
            this.GenerateKeys(new object[] { Encoding.ASCII.GetBytes(seed) });
        }

        public void GenerateKeys(object[] seeds)
        {
            var seed = seeds[0] as byte[];
            var key = seed.AsSpan(0, Math.Min(seed.Length, Cast5Cipher.MaxKeySize)).ToArray();
            this.Engine.Init(true, new KeyParameter(key));

            Array.Clear(this.DecryptionIV);
            Array.Clear(this.EncryptionIV);
            this.DecryptCount = 0;
            this.EncryptCount = 0;
        }

        public void Decrypt(Span<byte> src, Span<byte> dst)
        {
            for (int i = 0; i < src.Length; i++)
            {
                if (this.DecryptCount == 0)
                    this.Engine.ProcessBlock(this.DecryptionIV, 0, this.DecryptionIV, 0);

                byte cipherText = src[i];
                dst[i] = (byte)(cipherText ^ this.DecryptionIV[this.DecryptCount]);
                this.DecryptionIV[this.DecryptCount] = cipherText;
                this.DecryptCount = (this.DecryptCount + 1) & (Cast5Cipher.BlockSize - 1);
            }
        }

        public void Encrypt(Span<byte> src, Span<byte> dst)
        {
            for (int i = 0; i < src.Length; i++)
            {
                if (this.EncryptCount == 0)
                    this.Engine.ProcessBlock(this.EncryptionIV, 0, this.EncryptionIV, 0);

                dst[i] = (byte)(src[i] ^ this.EncryptionIV[this.EncryptCount]);
                this.EncryptionIV[this.EncryptCount] = dst[i];
                this.EncryptCount = (this.EncryptCount + 1) & (Cast5Cipher.BlockSize - 1);
            }
        }
    }
}
