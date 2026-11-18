namespace Comet.Network.Security.Tests
{
    using System;
    using System.Text;
    using Org.BouncyCastle.Crypto.Engines;
    using Org.BouncyCastle.Crypto.Parameters;
    using Xunit;

    /// <summary>
    /// The 5619 game client encrypts traffic with OpenSSL's CAST5 in CFB64 mode. The
    /// expected ciphertexts below were produced by OpenSSL itself:
    /// <c>openssl enc -provider legacy -cast5-cfb -K (key) -iv 0000000000000000</c>.
    /// </summary>
    public class Cast5CipherTests
    {
        const string Plaintext = "Hello TQ client, this is 37 bytes...!";
        const string ExpectedDefaultKeyCiphertext =
            "6D067D1ECA097FDCC78020A157F9318F8BD5809F2EF79FD4503B874EA9F7696A459E43965E";

        [Fact]
        public void BlockCipherMatchesRfc2144()
        {
            // RFC 2144, appendix B.1, 128-bit key
            var engine = new Cast5Engine();
            engine.Init(true, new KeyParameter(Convert.FromHexString("0123456712345678234567893456789A")));
            var block = Convert.FromHexString("0123456789ABCDEF");
            engine.ProcessBlock(block, 0, block, 0);
            Assert.Equal("238B4FE5847E44B2", Convert.ToHexString(block));
        }

        [Fact]
        public void EncryptMatchesOpenSslWithDefaultKey()
        {
            var cipher = new Cast5Cipher();
            var buffer = Encoding.ASCII.GetBytes(Plaintext);
            cipher.Encrypt(buffer, buffer);
            Assert.Equal(ExpectedDefaultKeyCiphertext, Convert.ToHexString(buffer));
        }

        [Fact]
        public void DecryptMatchesOpenSslWithDefaultKey()
        {
            var cipher = new Cast5Cipher();
            var buffer = Convert.FromHexString(ExpectedDefaultKeyCiphertext);
            cipher.Decrypt(buffer, buffer);
            Assert.Equal(Plaintext, Encoding.ASCII.GetString(buffer));
        }

        [Fact]
        public void StreamStateCarriesAcrossCalls()
        {
            // Packets arrive in pieces; the CFB position must carry over between calls
            var cipher = new Cast5Cipher();
            var buffer = Convert.FromHexString(ExpectedDefaultKeyCiphertext);
            cipher.Decrypt(buffer.AsSpan(0, 3), buffer.AsSpan(0, 3));
            cipher.Decrypt(buffer.AsSpan(3, 9), buffer.AsSpan(3, 9));
            cipher.Decrypt(buffer.AsSpan(12), buffer.AsSpan(12));
            Assert.Equal(Plaintext, Encoding.ASCII.GetString(buffer));
        }

        [Fact]
        public void LongKeysAreCutToSixteenBytes()
        {
            // The client passes the 64 character exchange key; OpenSSL uses 16 bytes of it
            var cipher = new Cast5Cipher();
            cipher.GenerateKeys(new object[] { Encoding.ASCII.GetBytes(
                Cast5Cipher.DefaultSeed + "this part is ignored by OpenSSL...") });
            var buffer = Encoding.ASCII.GetBytes(Plaintext);
            cipher.Encrypt(buffer, buffer);
            Assert.Equal(ExpectedDefaultKeyCiphertext, Convert.ToHexString(buffer));
        }

        [Fact]
        public void GenerateKeysResetsBothDirections()
        {
            var cipher = new Cast5Cipher();
            var scratch = new byte[5];
            cipher.Encrypt(scratch, scratch);
            cipher.Decrypt(scratch, scratch);

            cipher.GenerateKeys(new object[] { Encoding.ASCII.GetBytes(Cast5Cipher.DefaultSeed) });
            var buffer = Encoding.ASCII.GetBytes(Plaintext);
            cipher.Encrypt(buffer, buffer);
            Assert.Equal(ExpectedDefaultKeyCiphertext, Convert.ToHexString(buffer));
            cipher.Decrypt(buffer, buffer);
            Assert.Equal(Plaintext, Encoding.ASCII.GetString(buffer));
        }
    }
}
