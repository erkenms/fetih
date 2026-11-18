namespace Comet.Network.Security
{
    using System;
    using System.Linq;
    using System.Numerics;
    using System.Security.Cryptography;

    /// <summary>
    /// Server side of the SRP-6 password check used by the account server from patch 5619
    /// (TQ introduced it in 5532). The client never sends the password: the server stores
    /// a salt and verifier per account, sends a challenge, and checks the client's proof.
    /// </summary>
    /// <remarks>
    /// <para>5619: confirmed in VM. The scheme follows the well known World of Warcraft
    /// variant: N is a 256-bit prime, g = 7, k = 3, SHA-1 throughout, and the session key
    /// interleaves two SHA-1 digests of the shared secret. TQ's changes: the identity is
    /// left out of x, and the password is hashed with MD5 and a fixed salt first:</para>
    /// <code>
    /// p  = 00 00 00 00 || MD5(password || A3 AC A1 A3 "fdjf,jkgfkl")
    /// x  = SHA1(s || p)                     v = g^x mod N
    /// B  = k*v + g^b mod N                  u = SHA1(A || B)
    /// S  = (A * v^u)^b mod N                K = interleave(SHA1(even bytes of S), SHA1(odd))
    /// M1 = SHA1((SHA1(N) xor SHA1(g)) || SHA1(username) || s || A || B || K)
    /// </code>
    /// <para>Every number is hashed and sent as a little-endian byte array exactly as long
    /// as the number (the client's BigNumber class), and SHA-1 digests are read back as
    /// little-endian numbers.</para>
    /// </remarks>
    public sealed class Srp6
    {
        // Constants and static properties
        public const int SaltSize = 32;
        public const int PrivateKeySize = 32;
        public const int SessionSecretSize = 32;
        public static readonly BigInteger N = BigInteger.Parse(
            "0894B645E89E1535BBDAD5B8B290650530801B18EBFBF5E8FAB3C82872A3E9BB7",
            System.Globalization.NumberStyles.HexNumber);
        public static readonly BigInteger G = 7;
        public static readonly BigInteger K = 3;

        // Appended to the password before MD5 ("£¬¡£fdjf,jkgfkl" in the client's GBK)
        private static readonly byte[] PasswordSalt = new byte[] {
            0xA3, 0xAC, 0xA1, 0xA3, 0x66, 0x64, 0x6A, 0x66, 0x2C, 0x6A, 0x6B, 0x67, 0x66,
            0x6B, 0x6C
        };

        // Local fields and properties
        private readonly BigInteger PrivateKey;
        public BigInteger Salt { get; }
        public BigInteger Verifier { get; }
        public BigInteger ServerPublicKey { get; }
        public byte[] SessionKey { get; private set; }

        /// <summary>True if the last shared secret was shorter than 32 bytes.</summary>
        /// <remarks>
        /// The client always reads 32 bytes of the secret, past the end of a shorter one,
        /// so such a login (about 1 in 256) fails even with the right password.
        /// </remarks>
        public bool ShortSessionSecret { get; private set; }

        /// <summary>
        /// Instantiates a new SRP-6 session for one login attempt, computing the server's
        /// public key B from the account's verifier and a private key.
        /// </summary>
        /// <param name="salt">Account salt, little-endian</param>
        /// <param name="verifier">Account verifier, little-endian</param>
        /// <param name="privateKey">Private key b (little-endian), random if null</param>
        public Srp6(byte[] salt, byte[] verifier, byte[] privateKey = null)
        {
            this.Salt = Srp6.FromBytes(salt);
            this.Verifier = Srp6.FromBytes(verifier);
            this.PrivateKey = Srp6.FromBytes(
                privateKey ?? RandomNumberGenerator.GetBytes(Srp6.PrivateKeySize));
            this.ServerPublicKey = (Srp6.K * this.Verifier
                + BigInteger.ModPow(Srp6.G, this.PrivateKey, Srp6.N)) % Srp6.N;
        }

        /// <summary>The salt as the client hashes and receives it.</summary>
        public byte[] SaltBytes => Srp6.ToBytes(this.Salt);

        /// <summary>B as the client hashes and receives it.</summary>
        public byte[] ServerPublicKeyBytes => Srp6.ToBytes(this.ServerPublicKey);

        /// <summary>
        /// Generates a new random account salt. The top byte is never zero, so the salt
        /// has the same length as a number and as bytes.
        /// </summary>
        public static byte[] CreateSalt()
        {
            var salt = RandomNumberGenerator.GetBytes(Srp6.SaltSize);
            if (salt[^1] == 0) salt[^1] = 1;
            return salt;
        }

        /// <summary>Computes the account verifier v = g^x mod N for a password.</summary>
        /// <param name="salt">Account salt, little-endian</param>
        /// <param name="password">Password bytes as typed in the client</param>
        /// <returns>The verifier, little-endian.</returns>
        public static byte[] ComputeVerifier(byte[] salt, byte[] password)
        {
            var x = Srp6.FromBytes(SHA1.HashData(Srp6.Concat(
                Srp6.ToBytes(Srp6.FromBytes(salt)), Srp6.PasswordBlock(password))));
            return Srp6.ToBytes(BigInteger.ModPow(Srp6.G, x, Srp6.N));
        }

        /// <summary>
        /// The 20 byte password block the client hashes into x: four zero bytes and the
        /// MD5 of the password with TQ's fixed salt.
        /// </summary>
        /// <param name="password">Password bytes as typed in the client</param>
        public static byte[] PasswordBlock(byte[] password)
        {
            var block = new byte[20];
            MD5.HashData(Srp6.Concat(password, Srp6.PasswordSalt)).CopyTo(block, 4);
            return block;
        }

        /// <summary>
        /// Checks the client's proof. On success, <see cref="SessionKey"/> holds K.
        /// </summary>
        /// <param name="username">Account name bytes as sent by the client</param>
        /// <param name="clientPublicKey">A, little-endian</param>
        /// <param name="clientProof">M1, little-endian</param>
        /// <returns>True if the client knows the password.</returns>
        public bool Verify(byte[] username, byte[] clientPublicKey, byte[] clientProof)
        {
            var a = Srp6.FromBytes(clientPublicKey);
            if (a % Srp6.N == 0)
                return false;

            var u = Srp6.FromBytes(SHA1.HashData(Srp6.Concat(
                Srp6.ToBytes(a), Srp6.ToBytes(this.ServerPublicKey))));
            var secret = BigInteger.ModPow(
                a * BigInteger.ModPow(this.Verifier, u, Srp6.N) % Srp6.N,
                this.PrivateKey, Srp6.N);

            var secretBytes = Srp6.ToBytes(secret);
            this.ShortSessionSecret = secretBytes.Length < Srp6.SessionSecretSize;
            Array.Resize(ref secretBytes, Srp6.SessionSecretSize);
            var sessionKey = Srp6.Interleave(secretBytes);

            var hashN = SHA1.HashData(Srp6.ToBytes(Srp6.N));
            var hashG = SHA1.HashData(Srp6.ToBytes(Srp6.G));
            for (int i = 0; i < hashN.Length; i++) hashN[i] ^= hashG[i];

            var expected = Srp6.FromBytes(SHA1.HashData(Srp6.Concat(
                Srp6.ToBytes(Srp6.FromBytes(hashN)),
                Srp6.ToBytes(Srp6.FromBytes(SHA1.HashData(username))),
                Srp6.ToBytes(this.Salt),
                Srp6.ToBytes(a),
                Srp6.ToBytes(this.ServerPublicKey),
                Srp6.ToBytes(Srp6.FromBytes(sessionKey)))));

            if (expected != Srp6.FromBytes(clientProof))
                return false;

            this.SessionKey = sessionKey;
            return true;
        }

        /// <summary>
        /// Hashes the even and odd bytes of the secret separately and interleaves the two
        /// digests into a 40 byte key.
        /// </summary>
        internal static byte[] Interleave(byte[] secret)
        {
            var even = SHA1.HashData(secret.Where((_, i) => i % 2 == 0).ToArray());
            var odd = SHA1.HashData(secret.Where((_, i) => i % 2 == 1).ToArray());
            var key = new byte[even.Length * 2];
            for (int i = 0; i < even.Length; i++)
            {
                key[i * 2] = even[i];
                key[i * 2 + 1] = odd[i];
            }

            return key;
        }

        /// <summary>Reads little-endian bytes as a non-negative number.</summary>
        public static BigInteger FromBytes(ReadOnlySpan<byte> bytes) =>
            new BigInteger(bytes, isUnsigned: true, isBigEndian: false);

        /// <summary>Writes a number as little-endian bytes, as long as the number.</summary>
        public static byte[] ToBytes(BigInteger value) =>
            value.IsZero ? Array.Empty<byte>()
                : value.ToByteArray(isUnsigned: true, isBigEndian: false);

        internal static byte[] Concat(params byte[][] parts) =>
            parts.SelectMany(x => x).ToArray();
    }
}
