namespace Fetih.Network.Security.Tests
{
    using System;
    using System.Text;
    using Xunit;

    public class ClientCipherTests
    {
        [Fact]
        public void TQClientCipherUndoesServerEncryptionAcrossCalls()
        {
            var server = new TQCipher();
            var client = new TQClientCipher();
            for (int round = 0; round < 3; round++)
            {
                var toClient = Encoding.ASCII.GetBytes("server packet number " + round);
                var wire = (byte[])toClient.Clone();
                server.Encrypt(wire, wire);
                Assert.NotEqual(toClient, wire);
                client.Decrypt(wire, wire);
                Assert.Equal(toClient, wire);

                var toServer = Encoding.ASCII.GetBytes("client packet " + round + new string('x', 300));
                wire = (byte[])toServer.Clone();
                client.Encrypt(wire, wire);
                server.Decrypt(wire, wire);
                Assert.Equal(toServer, wire);
            }
        }

        [Fact]
        public void Srp6ClientMatchesClientModelVectors()
        {
            // Same inputs as Srp6Tests; a is little-endian
            var a = Convert.FromHexString(
                "5EED5EED5EED5EED5EED5EED5EED5EED5EED5EED5EED5E0DF0DDE0FE0FDCBA");
            var (publicKey, proof, sessionKey) = Srp6Client.ComputeProof(
                Encoding.ASCII.GetBytes("tester"), Encoding.ASCII.GetBytes("secret123"),
                Convert.FromHexString("4142434445464748494A4B4C4D4E4F505152535455565758595A5B5C5D5E5F60"),
                Convert.FromHexString("E9C761C1FF34B85A92F042C6F09AFF97AB46D07C711B1FF41B27B2DE3B209A51"),
                a);
            Assert.Equal("73A61B17F4B542FB46E22927B5F6996E36C5F8E77A40D9998181C0EDBA5FC680",
                Convert.ToHexString(publicKey));
            Assert.Equal("AD7DBD78A11B7E45BC64A70ADD51597308BC169B", Convert.ToHexString(proof));
            Assert.Equal(40, sessionKey.Length);
        }
    }
}
