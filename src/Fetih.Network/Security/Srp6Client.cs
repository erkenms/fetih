namespace Fetih.Network.Security
{
    using System;
    using System.Numerics;
    using System.Security.Cryptography;

    public static class Srp6Client
    {
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
