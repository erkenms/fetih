namespace Comet.Network.Packets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    public sealed class PacketWriter : BinaryWriter, IDisposable
    {
        public PacketWriter() : base(new MemoryStream())
        {
            base.Write((ushort)0);
        }

        public void Write(string value, int fixedLength)
        {
            var array = new byte[fixedLength];
            Encoding.ASCII.GetBytes(value).CopyTo(array, 0);
            base.Write(array);
        }

        public void Write(List<string> strings)
        {
            base.Write((byte)strings.Count);
            for (int i = 0; i < strings.Count; i++)
            {
                var bytes = Encoding.Latin1.GetBytes(strings[i] ?? string.Empty);
                int length = Math.Min(bytes.Length, byte.MaxValue);
                base.Write((byte)length);
                base.Write(bytes, 0, length);
            }
        }

        public byte[] ToArray()
        {
            base.BaseStream.Flush();
            return (base.BaseStream as MemoryStream).ToArray();
        }

        #region IDisposable Support
        private bool DisposedValue = false;

        private new void Dispose(bool disposing)
        {
            if (!this.DisposedValue)
            {
                if (disposing)
                {
                    base.BaseStream.Close();
                    base.BaseStream.Dispose();
                }

                base.Dispose(disposing);
                this.DisposedValue = true;
            }
        }

        public new void Dispose()
        {
            this.Dispose(true);
        }
        #endregion
    }
}
