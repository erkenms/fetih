namespace Comet.Game.Items
{
    // Client data files like itemtype.dat: each byte is XORed with a 128-byte key from the
    // MSVC rand() sequence seeded with 0x2537, then rotated right by its offset modulo 8.
    public static class TqFileCipher
    {
        private static readonly byte[] Key = CreateKey(0x2537);

        public static void Decrypt(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                int b = data[i] ^ Key[i % Key.Length];
                int shift = i % 8;
                data[i] = (byte)((b >> shift) | (b << (8 - shift)));
            }
        }

        private static byte[] CreateKey(uint seed)
        {
            var key = new byte[128];
            for (int i = 0; i < key.Length; i++)
            {
                seed = seed * 214013 + 2531011;
                key[i] = (byte)((seed >> 16) & 0x7FFF);
            }
            return key;
        }
    }
}
