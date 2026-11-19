namespace Comet.Network.Packets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    public sealed class PacketReader : BinaryReader, IDisposable
    {
        public PacketReader(byte[] bytes) : base(new MemoryStream(bytes))
        {
        }

        public override string ReadString()
        {
            return base.ReadString().TrimEnd('\0');
        }

        public string ReadString(int fixedLength)
        {
            return Encoding.ASCII.GetString(base.ReadBytes(fixedLength)).TrimEnd('\0');
        }

        public List<string> ReadStrings()
        {
            var strings = new List<string>();
            var amount = base.ReadByte();
            for (int i = 0; i < amount; i++)
                strings.Add(this.ReadString());
            return strings;
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
