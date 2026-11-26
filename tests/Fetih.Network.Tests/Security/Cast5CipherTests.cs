namespace Fetih.Network.Security.Tests
{
    using System;
    using System.Text;
    using Xunit;

    public class Cast5CipherTests
    {
        const string Plaintext = "Hello TQ client, this is 37 bytes...!";
        const string ExpectedDefaultKeyCiphertext =
            "6D067D1ECA097FDCC78020A157F9318F8BD5809F2EF79FD4503B874EA9F7696A459E43965E";

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
