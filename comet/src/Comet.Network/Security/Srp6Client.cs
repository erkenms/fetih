namespace Comet.Network.Security
{
    using System;
    using System.Numerics;
    using System.Security.Cryptography;

    /// <summary>
    /// Client side of the 5619 SRP-6 login, for test clients and bots. Computes the
    /// client's public key A and proof M1 from the password and the server's challenge,
    /// the same way the game client does (see <see cref="Srp6"/> for the formulas).
    /// </summary>
    public static class Srp6Client
    {
        /// <summary>
        /// Computes the client's answer to a login challenge.
        /// </summary>
        /// <param name="username">Account name bytes as sent in MsgAccountSRP6</param>
        /// <param name="password">Password bytes as typed in the client</param>
        /// <param name="salt">Account salt from the challenge, little-endian</param>
        /// <param name="serverPublicKey">B from the challenge, little-endian</param>
        /// <param name="privateKey">Private key a (little-endian), random if null</param>
        /// <returns>A and M1 for MsgLoginProofC, and the session key K.</returns>
        /// <remarks>
        /// The game client hashes 32 bytes of the shared secret even when it's shorter,
        /// reading past its end, so about 1 real login in 256 fails. This pads with zeros
        /// like the server, so test logins never fail that way.
        /// </remarks>
        public static (byte[] PublicKey, byte[] Proof, byte[] SessionKey) ComputeProof(
            byte[] username, byte[] password, byte[] salt, byte[] serverPublicKey,
            byte[] privateKey = null)
        {
            var s = Srp6.FromBytes(salt);
            var b = Srp6.FromBytes(serverPublicKey);
            var a = Srp6.FromBytes(privateKey ?? RandomNumberGenerator.GetBytes(Srp6.PrivateKeySize));
            var x = Srp6.FromBytes(SHA1.HashData(Srp6.Concat(
                Srp6.ToBytes(s), Srp6.PasswordBlock(password))));

            var publicKey = BigInteger.ModPow(Srp6.G, a, Srp6.N);
            var u = Srp6.FromBytes(SHA1.HashData(Srp6.Concat(
                Srp6.ToBytes(publicKey), Srp6.ToBytes(b))));
            var baseValue = ((b - Srp6.K * BigInteger.ModPow(Srp6.G, x, Srp6.N)) % Srp6.N
                + Srp6.N) % Srp6.N;
            var secret = BigInteger.ModPow(baseValue, a + u * x, Srp6.N);

            var secretBytes = Srp6.ToBytes(secret);
            Array.Resize(ref secretBytes, Srp6.SessionSecretSize);
            var sessionKey = Srp6.Interleave(secretBytes);

            var hashN = SHA1.HashData(Srp6.ToBytes(Srp6.N));
            var hashG = SHA1.HashData(Srp6.ToBytes(Srp6.G));
            for (int i = 0; i < hashN.Length; i++) hashN[i] ^= hashG[i];

            var proof = Srp6.FromBytes(SHA1.HashData(Srp6.Concat(
                Srp6.ToBytes(Srp6.FromBytes(hashN)),
                Srp6.ToBytes(Srp6.FromBytes(SHA1.HashData(username))),
                Srp6.ToBytes(s),
                Srp6.ToBytes(publicKey),
                Srp6.ToBytes(b),
                Srp6.ToBytes(Srp6.FromBytes(sessionKey)))));

            return (Srp6.ToBytes(publicKey), Srp6.ToBytes(proof), sessionKey);
        }
    }
}
