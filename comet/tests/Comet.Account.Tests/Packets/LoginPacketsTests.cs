namespace Comet.Account.Tests.Packets
{
    using System;
    using System.Text;
    using Comet.Account.Packets;
    using Comet.Network.Security;
    using Xunit;

    public class LoginPacketsTests
    {
        // The vectors of Srp6Tests (from Vectors/srp6_client.py); b is little-endian here
        static readonly byte[] Salt = Convert.FromHexString(
            "4142434445464748494A4B4C4D4E4F505152535455565758595A5B5C5D5E5F60");
        static readonly byte[] Verifier = Convert.FromHexString(
            "3C2CF752CE5D738BFAFAAB701E8FDEB305B043FBBBB4A8B7426BFE41E8C6003A");
        static readonly byte[] ServerPrivate = Convert.FromHexString(
            "FFEEDDCCBBAA99887766554433221100EFCDAB896745230188796A5B4C3D2E1F");
        static readonly byte[] ClientPublic = Convert.FromHexString(
            "73A61B17F4B542FB46E22927B5F6996E36C5F8E77A40D9998181C0EDBA5FC680");
        static readonly byte[] ClientProof = Convert.FromHexString(
            "AD7DBD78A11B7E45BC64A70ADD51597308BC169B");

        private static byte[] WithFooter(byte[] packet)
        {
            var bytes = new byte[packet.Length + 8];
            packet.CopyTo(bytes, 0);
            return bytes;
        }

        [Fact]
        public void DecodesAccountSrp6()
        {
            var bytes = new byte[240];
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)240);
            BitConverter.TryWriteBytes(bytes.AsSpan(2), (ushort)1124);
            Encoding.ASCII.GetBytes("tester").CopyTo(bytes, 8);
            Encoding.ASCII.GetBytes("Altay").CopyTo(bytes, 136);

            var msg = new MsgAccountSRP6();
            msg.Decode(bytes);
            Assert.Equal("tester", msg.Username);
            Assert.Equal(Encoding.ASCII.GetBytes("tester"), msg.UsernameBytes);
            Assert.Equal("Altay", msg.Realm);
        }

        [Fact]
        public void ChallengeHasBAndSaltWhereClientReadsThem()
        {
            var srp = new Srp6(Salt, Verifier, ServerPrivate);
            var bytes = new MsgLoginChallengeS(srp).Encode();

            Assert.Equal(MsgLoginChallengeS.Size, bytes.Length);
            Assert.Equal(520, bytes.Length);
            Assert.Equal(1213, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(32, bytes[4]);
            Assert.Equal(
                "E9C761C1FF34B85A92F042C6F09AFF97AB46D07C711B1FF41B27B2DE3B209A51",
                Convert.ToHexString(bytes, 5, 32));
            Assert.Equal(1, bytes[0x85]);
            Assert.Equal(7, bytes[0x86]);
            Assert.Equal(32, bytes[0x106]);
            Assert.Equal(0xB7, bytes[0x107]);
            Assert.Equal(32, bytes[0x187]);
            Assert.Equal(Salt, bytes.AsSpan(0x188, 32).ToArray());
        }

        [Fact]
        public void DecodesProofFields()
        {
            var bytes = new byte[262];
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)262);
            BitConverter.TryWriteBytes(bytes.AsSpan(2), (ushort)1214);
            bytes[4] = (byte)ClientPublic.Length;
            ClientPublic.CopyTo(bytes, 5);
            bytes[0x85] = (byte)ClientProof.Length;
            ClientProof.CopyTo(bytes, 0x86);

            var msg = new MsgLoginProofC();
            msg.Decode(WithFooter(bytes));
            Assert.Equal(ClientPublic, msg.ClientPublicKey);
            Assert.Equal(ClientProof, msg.ClientProof);
        }

        [Fact]
        public void ProofWithOversizedFieldIsRejected()
        {
            var bytes = new byte[262];
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)262);
            BitConverter.TryWriteBytes(bytes.AsSpan(2), (ushort)1214);
            bytes[4] = 200;
            Assert.Throws<ArgumentOutOfRangeException>(() => new MsgLoginProofC().Decode(bytes));
        }

        [Fact]
        public void ConnectExAcceptHasTokenHalvesAndBothAddresses()
        {
            var bytes = new MsgConnectEx("192.168.1.20", 5816, 0x1122334455667788).Encode();
            Assert.Equal(88, bytes.Length);
            Assert.Equal(1055, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(0x55667788u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(0x11223344u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(5816, BitConverter.ToUInt16(bytes, 12));
            Assert.Equal("192.168.1.20", Encoding.ASCII.GetString(bytes, 20, 12));
            Assert.Equal(0, bytes[32]);
            Assert.Equal(5816, BitConverter.ToUInt16(bytes, 52));
            Assert.Equal("192.168.1.20", Encoding.ASCII.GetString(bytes, 56, 12));
        }

        [Fact]
        public void ConnectExRejectHasCodeAtEight()
        {
            var bytes = new MsgConnectEx(MsgConnectEx.RejectionCode.InvalidPassword).Encode();
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(1u, BitConverter.ToUInt32(bytes, 8));
        }
    }
}
