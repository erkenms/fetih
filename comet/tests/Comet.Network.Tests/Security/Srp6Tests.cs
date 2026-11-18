namespace Comet.Network.Security.Tests
{
    using System;
    using System.Text;
    using Xunit;

    /// <summary>
    /// The account server's SRP-6 check must accept proofs from the 5619 client, which
    /// can only be tried by hand. The expected values come from Vectors/srp6_client.py,
    /// an independent Python model of the client written from its disassembly.
    /// </summary>
    public class Srp6Tests
    {
        static readonly byte[] Username = Encoding.ASCII.GetBytes("tester");
        static readonly byte[] Password = Encoding.ASCII.GetBytes("secret123");
        static readonly byte[] Salt = Convert.FromHexString(
            "4142434445464748494A4B4C4D4E4F505152535455565758595A5B5C5D5E5F60");
        const string Verifier =
            "3C2CF752CE5D738BFAFAAB701E8FDEB305B043FBBBB4A8B7426BFE41E8C6003A";

        // b is written big-endian in the script; the server takes little-endian bytes
        static readonly byte[] ServerPrivate = Reverse(Convert.FromHexString(
            "1F2E3D4C5B6A79880123456789ABCDEF00112233445566778899AABBCCDDEEFF"));
        const string ServerPublic =
            "E9C761C1FF34B85A92F042C6F09AFF97AB46D07C711B1FF41B27B2DE3B209A51";
        const string ClientPublic =
            "73A61B17F4B542FB46E22927B5F6996E36C5F8E77A40D9998181C0EDBA5FC680";
        const string ClientProof = "AD7DBD78A11B7E45BC64A70ADD51597308BC169B";
        const string SessionKey =
            "779FC0771A70CE44BBA06C9162ABCEE95D3586507A7B9598115C58D8E5DFA0D8" +
            "16620D68A3D1E1BD";

        private static byte[] Reverse(byte[] bytes)
        {
            Array.Reverse(bytes);
            return bytes;
        }

        private static Srp6 NewSession() =>
            new Srp6(Salt, Convert.FromHexString(Verifier), ServerPrivate);

        [Fact]
        public void PasswordBlockIsPaddedMd5()
        {
            Assert.Equal("00000000938BEE2D365DE90199800B6EEB65930F",
                Convert.ToHexString(Srp6.PasswordBlock(Password)));
        }

        [Fact]
        public void VerifierMatchesClientModel()
        {
            Assert.Equal(Verifier, Convert.ToHexString(Srp6.ComputeVerifier(Salt, Password)));
        }

        [Fact]
        public void ServerPublicKeyMatchesClientModel()
        {
            Assert.Equal(ServerPublic, Convert.ToHexString(NewSession().ServerPublicKeyBytes));
        }

        [Fact]
        public void AcceptsClientProof()
        {
            var srp = NewSession();
            Assert.True(srp.Verify(Username, Convert.FromHexString(ClientPublic),
                Convert.FromHexString(ClientProof)));
            Assert.Equal(SessionKey, Convert.ToHexString(srp.SessionKey));
            Assert.False(srp.ShortSessionSecret);
        }

        [Fact]
        public void RejectsWrongProof()
        {
            var proof = Convert.FromHexString(ClientProof);
            proof[0] ^= 1;
            Assert.False(NewSession().Verify(Username, Convert.FromHexString(ClientPublic), proof));
        }

        [Fact]
        public void RejectsOtherUsername()
        {
            Assert.False(NewSession().Verify(Encoding.ASCII.GetBytes("Tester"),
                Convert.FromHexString(ClientPublic), Convert.FromHexString(ClientProof)));
        }

        [Fact]
        public void RejectsZeroOrModulusPublicKey()
        {
            var proof = Convert.FromHexString(ClientProof);
            Assert.False(NewSession().Verify(Username, Array.Empty<byte>(), proof));
            Assert.False(NewSession().Verify(Username, Srp6.ToBytes(Srp6.N), proof));
        }

        [Fact]
        public void WrongPasswordVerifierRejectsProof()
        {
            var verifier = Srp6.ComputeVerifier(Salt, Encoding.ASCII.GetBytes("secret124"));
            var srp = new Srp6(Salt, verifier, ServerPrivate);
            Assert.False(srp.Verify(Username, Convert.FromHexString(ClientPublic),
                Convert.FromHexString(ClientProof)));
        }

        [Fact]
        public void CreatedSaltsKeepTheirLength()
        {
            for (int i = 0; i < 64; i++)
            {
                var salt = Srp6.CreateSalt();
                Assert.Equal(Srp6.SaltSize, salt.Length);
                Assert.Equal(Srp6.SaltSize, Srp6.ToBytes(Srp6.FromBytes(salt)).Length);
            }
        }
    }
}
