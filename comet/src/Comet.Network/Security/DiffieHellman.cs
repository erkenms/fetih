namespace Comet.Network.Security
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Network.Services;
    using Org.BouncyCastle.Math;
    using Org.BouncyCastle.Utilities.Encoders;

    public sealed class DiffieHellman
    {
        public readonly static PrimeGeneratorService ProbablePrimes;
        private const string DefaultGenerator = "05";
        private const string DefaultPrimativeRoot = 
            "E7A69EBDF105F2A6BBDEAD7E798F76A209AD73FB466431E2E7352ED262F8C558" +
            "F10BEFEA977DE9E21DCEE9B04D245F300ECCBBA03E72630556D011023F9E857F";

        public BigInteger PrimeRoot { get; set; }
        public BigInteger Generator { get; set; }
        public BigInteger Modulus { get; set; }
        public BigInteger PublicKey { get; private set; }
        public BigInteger PrivateKey { get; private set; }

        static DiffieHellman()
        {
            DiffieHellman.ProbablePrimes = new PrimeGeneratorService();
        }

        public DiffieHellman(
            string p = DiffieHellman.DefaultPrimativeRoot, 
            string g = DiffieHellman.DefaultGenerator)
        {
            this.PrimeRoot = new BigInteger(p, 16);
            this.Generator = new BigInteger(g, 16);
        }

        public async Task ComputePublicKeyAsync()
        {
            if (this.Modulus == null)
            {
                this.Modulus = await DiffieHellman.ProbablePrimes.NextAsync();
            }

            this.PublicKey = this.Generator.ModPow(this.Modulus, this.PrimeRoot);
        }

        public void ComputePrivateKey(string clientKeyString)
        {
            BigInteger clientKey = new BigInteger(clientKeyString, 16);
            this.PrivateKey = clientKey.ModPow(this.Modulus, this.PrimeRoot);
        }

        // The secret's big-endian bytes, cut at the first zero byte, are hashed with MD5 to hex s1;
        // the key is s1 + hex MD5(s1 + s1). The cipher uses the first 16 characters.
        public byte[] ComputeCipherKey() =>
            DiffieHellman.DeriveCipherKey(this.PrivateKey.ToByteArrayUnsigned());

        public static byte[] DeriveCipherKey(byte[] sharedSecret)
        {
            int length = Array.IndexOf(sharedSecret, (byte)0);
            if (length < 0) length = sharedSecret.Length;

            string first = DiffieHellman.HexMD5(sharedSecret.AsSpan(0, length));
            string second = DiffieHellman.HexMD5(Encoding.ASCII.GetBytes(first + first));
            return Encoding.ASCII.GetBytes(first + second);
        }

        private static string HexMD5(ReadOnlySpan<byte> data) =>
            Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();
    }
}
