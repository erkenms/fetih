namespace Comet.Network.Security
{
    using System;
    using System.Text;
    using Org.BouncyCastle.Crypto.Engines;
    using Org.BouncyCastle.Crypto.Parameters;

    /// <summary>
    /// CAST-128 (RFC 2144) in CFB64 mode, used on the game server from patch 5619 in place
    /// of <see cref="BlowfishCipher"/>. The client uses OpenSSL's CAST_set_key and
    /// CAST_cfb64_encrypt: keys longer than 16 bytes are cut to 16, and both directions
    /// start from an all-zero IV. Generating new keys after the key exchange resets the
    /// IVs as well.
    /// </summary>
    /// <remarks>
    /// 5619: from client binary (initial key, mode and IV), and the reference server (mode
    /// and rekeying).
    /// </remarks>
    public sealed class Cast5Cipher : ICipher
    {
        // Constants and static properties
        public const int BlockSize = 8;
        public const int MaxKeySize = 16;
        public const string DefaultSeed = "BC234xs45nme7HU9";

        // Local fields and properties
        private readonly Cast5Engine Engine;
        private readonly byte[] DecryptionIV, EncryptionIV;
        private int DecryptCount, EncryptCount;

        /// <summary>
        /// Instantiates a new instance of <see cref="Cast5Cipher"/> and generates keys
        /// from the seed. Without a seed, uses the client's initial key, which encrypts the
        /// key exchange.
        /// </summary>
        /// <param name="seed">Key for the cipher</param>
        public Cast5Cipher(string seed = Cast5Cipher.DefaultSeed)
        {
            this.Engine = new Cast5Engine();
            this.DecryptionIV = new byte[Cast5Cipher.BlockSize];
            this.EncryptionIV = new byte[Cast5Cipher.BlockSize];
            this.GenerateKeys(new object[] { Encoding.ASCII.GetBytes(seed) });
        }

        /// <summary>
        /// Generates keys from the first seed (a byte array) and resets both IVs to zero.
        /// Only the first 16 bytes of the seed are used, as in OpenSSL.
        /// </summary>
        /// <param name="seeds">An array of seeds used to generate keys</param>
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

        /// <summary>
        /// Decrypts bytes using cipher feedback mode. The source and destination may be
        /// the same slice, but otherwise should not overlap.
        /// </summary>
        /// <param name="src">Source span that requires decrypting</param>
        /// <param name="dst">Destination span to contain the decrypted result</param>
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

        /// <summary>
        /// Encrypts bytes using cipher feedback mode. The source and destination may be
        /// the same slice, but otherwise should not overlap.
        /// </summary>
        /// <param name="src">Source span that requires encrypting</param>
        /// <param name="dst">Destination span to contain the encrypted result</param>
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
