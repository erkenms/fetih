namespace Fetih.Network.Security
{
    using System;
    using System.Linq;
    using System.Numerics;
    using System.Security.Cryptography;

    // WoW-style SRP6 (256-bit N, g = 7, k = 3, SHA-1), but x leaves out the identity and the
    // password is pre-hashed:
    // p  = 00 00 00 00 || MD5(password || A3 AC A1 A3 "fdjf,jkgfkl")
    // x  = SHA1(s || p)                     v = g^x mod N
    // B  = k*v + g^b mod N                  u = SHA1(A || B)
    // S  = (A * v^u)^b mod N                K = interleave(SHA1(even bytes of S), SHA1(odd))
    // M1 = SHA1((SHA1(N) xor SHA1(g)) || SHA1(username) || s || A || B || K)
    // Numbers are hashed and sent as little-endian bytes exactly as long as the number, and
    // digests are read back as little-endian numbers.
    public sealed class Srp6
    {
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

        private readonly BigInteger PrivateKey;
        public BigInteger Salt { get; }
        public BigInteger Verifier { get; }
        public BigInteger ServerPublicKey { get; }
        public byte[] SessionKey { get; private set; }

        // The client always hashes 32 bytes of S, reading past a shorter one, so such a login
        // (about 1 in 256) fails even with the right password.
        public bool ShortSessionSecret { get; private set; }

        public Srp6(byte[] salt, byte[] verifier, byte[] privateKey = null)
        {
            this.Salt = Srp6.FromBytes(salt);
            this.Verifier = Srp6.FromBytes(verifier);
            this.PrivateKey = Srp6.FromBytes(
                privateKey ?? RandomNumberGenerator.GetBytes(Srp6.PrivateKeySize));
            this.ServerPublicKey = (Srp6.K * this.Verifier
                + BigInteger.ModPow(Srp6.G, this.PrivateKey, Srp6.N)) % Srp6.N;
        }

        public byte[] SaltBytes => Srp6.ToBytes(this.Salt);

        public byte[] ServerPublicKeyBytes => Srp6.ToBytes(this.ServerPublicKey);

        // Top byte never zero, so the salt is as long as a number as it is as bytes.
        public static byte[] CreateSalt()
        {
            var salt = RandomNumberGenerator.GetBytes(Srp6.SaltSize);
            if (salt[^1] == 0) salt[^1] = 1;
            return salt;
        }

        public static byte[] ComputeVerifier(byte[] salt, byte[] password)
        {
            var x = Srp6.FromBytes(SHA1.HashData(Srp6.Concat(
                Srp6.ToBytes(Srp6.FromBytes(salt)), Srp6.PasswordBlock(password))));
            return Srp6.ToBytes(BigInteger.ModPow(Srp6.G, x, Srp6.N));
        }

        public static byte[] PasswordBlock(byte[] password)
        {
            var block = new byte[20];
            MD5.HashData(Srp6.Concat(password, Srp6.PasswordSalt)).CopyTo(block, 4);
            return block;
        }

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

        public static BigInteger FromBytes(ReadOnlySpan<byte> bytes) =>
            new BigInteger(bytes, isUnsigned: true, isBigEndian: false);

        public static byte[] ToBytes(BigInteger value) =>
            value.IsZero ? Array.Empty<byte>()
                : value.ToByteArray(isUnsigned: true, isBigEndian: false);

        internal static byte[] Concat(params byte[][] parts) =>
            parts.SelectMany(x => x).ToArray();
    }
}
