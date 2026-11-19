namespace Comet.Network.Security
{
    using System;

    public sealed class TQCipher : ICipher
    {
        internal static readonly byte[] KInit = new byte[0x200];
        
        private byte[] K;
        private byte[] K1 = new byte[0x200];
        private byte[] K2 = new byte[0x200];
        private ushort DecryptCounter, EncryptCounter;
        
        public Increment Add;

        static TQCipher()
        {
            var seed = new byte[] { 0x9D, 0x0F, 0xFA, 0x13, 0x62, 0x79, 0x5C, 0x6D };
            for (int i = 0; i < 0x100; i++) 
            {
                TQCipher.KInit[i] = seed[0];
                TQCipher.KInit[i + 0x100] = seed[4];
                seed[0] = (byte)((seed[1] + (seed[0] * seed[2])) * seed[0] + seed[3]);
                seed[4] = (byte)((seed[5] - (seed[4] * seed[6])) * seed[4] + seed[7]);
            }
        }

        public TQCipher()
        {
            this.Add = this.DefaultIncrement;
            Buffer.BlockCopy(TQCipher.KInit, 0, this.K1, 0, TQCipher.KInit.Length);
            Buffer.BlockCopy(TQCipher.KInit, 0, this.K2, 0, TQCipher.KInit.Length);
            this.K = this.K1;
        }

        public void GenerateKeys(object[] seeds)
        {
            var seed = seeds[0] as ulong?;
            var a = (uint)(seed >> 32);
            var b = (uint)(seed);
            var c = (uint)(((a + b) ^ 0x4321) ^ a);
            var d = (uint)(c * c);

            var temp1 = BitConverter.GetBytes(c);
            var temp2 = BitConverter.GetBytes(d);
            for (int i = 0; i < 0x100; i++)
            {
                this.K2[i] = (byte)(this.K1[i] ^ temp1[i % 4]);
                this.K2[i + 0x100] = (byte)(this.K1[i + 0x100] ^ temp2[i % 4]);
            }

            this.K = this.K2;
            this.EncryptCounter = 0;
        }

        public void Decrypt(Span<byte> src, Span<byte> dst)
        {
            this.XOR(src, dst, this.K, ref this.DecryptCounter);
        }

        public void Encrypt(Span<byte> src, Span<byte> dst)
        {
            this.XOR(src, dst, this.K1, ref this.EncryptCounter);
        }

        private void XOR(Span<byte> src, Span<byte> dst, byte[] k, ref ushort c)
        {
            var x = this.Add(ref c, src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                dst[i] = (byte)(src[i] ^ 0xAB);
                dst[i] = (byte)(dst[i] >> 4 | dst[i] << 4);
                dst[i] = (byte)(dst[i] ^ k[x & 0xff]);
                dst[i] = (byte)(dst[i] ^ k[(x >> 8) + 0x100]);
                x++;
            }
        }

        public delegate ushort Increment(ref ushort x, int n);

        public ushort DefaultIncrement(ref ushort x, int n) 
            => (ushort)((x = (ushort)(x + n)) - n);
    }
}