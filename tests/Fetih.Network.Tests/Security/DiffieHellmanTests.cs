namespace Fetih.Network.Security.Tests
{
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using Org.BouncyCastle.Math;
    using Xunit;

    public class DiffieHellmanTests
    {
        // From the original Comet (e5d27df), described there as the results of the client's
        // OpenSSL library; the cipher key was added with the CAST5 switch (294da89).
        const string ClientKey =
            "59c152b3fa860f115316b5458f604e90f8ee9b253ecc137f46728a7701afb35b" +
            "861c5b97a1d40b3640efc96782f0b2971c61bd5606944e7cf3015da456eeba62";
        const string Modulus =
            "aafe7d12da3aad2d3b4957beb8887f02535a858cffbc0d7514ca2a413255a249";

        const string ExpectedPublicKey =
            "710f779e2d4d6d763de21082fd21202c93ede9c97ecaeb48e290444e7685b590" +
            "511884d2bfb7c8f29b8aedd19a6cf6951519f3a123385910a3dac97a363de44e";
        const string ExpectedSharedSecret =
            "01805cfc75fa3834ae160015c5bd7d6883eea835bfe6b277b6cd7968e9bccb60" +
            "28197772c760abd280419cc52245492511df455cfb1578ab2d58c2e1289d1853";

        private static string Hex(BigInteger value) =>
            Convert.ToHexString(value.ToByteArrayUnsigned()).ToLowerInvariant();

        [Fact]
        public async Task ComputesPublicKeySharedSecretAndCipherKeyAsync()
        {
            DiffieHellman dh = new DiffieHellman();
            dh.Modulus = new BigInteger(DiffieHellmanTests.Modulus, 16);
            await dh.ComputePublicKeyAsync();
            Assert.Equal(ExpectedPublicKey, Hex(dh.PublicKey));

            dh.ComputePrivateKey(DiffieHellmanTests.ClientKey);
            Assert.Equal(ExpectedSharedSecret, Hex(dh.PrivateKey));

            // The shared secret above has a zero byte at offset 10, so only the first ten
            // bytes are hashed (the client reads it as a C string).
            Assert.Equal(
                "c5eb5aceef378461c0f9c4c8f8e75c304011aacada9b3e1442439081840f4c19",
                Encoding.ASCII.GetString(dh.ComputeCipherKey()));
        }

        [Fact]
        public void DeriveCipherKeyHashesWholeSecretWithoutZeroByte()
        {
            // Expected: md5(secret) as s1, then s1 + md5(s1 + s1), both lowercase hex
            var secret = new byte[32];
            for (int i = 0; i < secret.Length; i++) secret[i] = (byte)(i + 1);
            Assert.Equal(
                "5985331bcec971b262122aa1ca5ad4114c855ff0bfb0262b4c9f95161a88e073",
                Encoding.ASCII.GetString(DiffieHellman.DeriveCipherKey(secret)));
        }
    }
}
