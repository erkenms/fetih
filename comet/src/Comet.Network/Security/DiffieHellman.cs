namespace Comet.Network.Security
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Network.Services;
    using Org.BouncyCastle.Math;
    using Org.BouncyCastle.Utilities.Encoders;

    /// <summary>
    /// This implementation of the Diffie Hellman Key Exchange implements the base modulo
    /// and big number operations. The shared secret isn't used as the cipher key directly:
    /// from patch 5619 it is post-processed with MD5 (see <see cref="ComputeCipherKey"/>).
    /// </summary>
    public sealed class DiffieHellman
    {
        // Constants and static properties
        public readonly static PrimeGeneratorService ProbablePrimes;
        private const string DefaultGenerator = "05";
        private const string DefaultPrimativeRoot = 
            "E7A69EBDF105F2A6BBDEAD7E798F76A209AD73FB466431E2E7352ED262F8C558" +
            "F10BEFEA977DE9E21DCEE9B04D245F300ECCBBA03E72630556D011023F9E857F";

        // Key exchange Properties
        public BigInteger PrimeRoot { get; set; }
        public BigInteger Generator { get; set; }
        public BigInteger Modulus { get; set; }
        public BigInteger PublicKey { get; private set; }
        public BigInteger PrivateKey { get; private set; }

        /// <summary>
        /// Generate the modulus integer as a static constant. This is an unfortunate
        /// consequence of creating a randomness service for generating numbers in a
        /// thread safe environment, and using a language with poor multithreading 
        /// support.
        /// </summary>
        static DiffieHellman()
        {
            DiffieHellman.ProbablePrimes = new PrimeGeneratorService();
        }

        /// <summary>
        /// Instantiates a new instance of the <see cref="DiffieHellman"/> key exchange.
        /// If no prime root or generator is specified, then defaults for remaining W
        /// interoperable with the Conquer Online game client will be used. 
        /// </summary>
        /// <param name="p">Prime root to modulo with the generated probable prime.</param>
        /// <param name="g">Generator used to seed the modulo of primes.</param>
        public DiffieHellman(
            string p = DiffieHellman.DefaultPrimativeRoot, 
            string g = DiffieHellman.DefaultGenerator)
        {
            this.PrimeRoot = new BigInteger(p, 16);
            this.Generator = new BigInteger(g, 16);
        }

        /// <summary>Computes the public key for sending to the client.</summary>
        public async Task ComputePublicKeyAsync()
        {
            // Check for null to allow for testability from unit tests.
            if (this.Modulus == null)
            {
                this.Modulus = await DiffieHellman.ProbablePrimes.NextAsync();
            }

            this.PublicKey = this.Generator.ModPow(this.Modulus, this.PrimeRoot);
        }

        /// <summary>Computes the private key given the client response.</summary>
        /// <param name="clientKeyString">Client key from the exchange</param>
        /// <remarks>The shared secret is stored in <see cref="PrivateKey"/>.</remarks>
        public void ComputePrivateKey(string clientKeyString)
        {
            BigInteger clientKey = new BigInteger(clientKeyString, 16);
            this.PrivateKey = clientKey.ModPow(this.Modulus, this.PrimeRoot);
        }

        /// <summary>
        /// Derives the game cipher key from the shared secret the way the 5619 client does:
        /// the secret's big-endian bytes are read as a C string (cut at the first zero
        /// byte) and hashed with MD5. The lowercase hex digest, s1, is followed by the hex
        /// MD5 of s1 + s1, giving 64 ASCII characters. The cipher uses the first 16.
        /// </summary>
        /// <remarks>5619: confirmed in VM.</remarks>
        /// <returns>The 64 byte ASCII key for <see cref="Cast5Cipher"/>.</returns>
        public byte[] ComputeCipherKey() =>
            DiffieHellman.DeriveCipherKey(this.PrivateKey.ToByteArrayUnsigned());

        /// <summary>Derives the game cipher key from raw shared secret bytes.</summary>
        /// <param name="sharedSecret">Big-endian shared secret, without padding</param>
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
