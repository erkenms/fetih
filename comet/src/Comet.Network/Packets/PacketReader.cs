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

        // BinaryReader.ReadBytes returns what is left of a truncated packet; a short field is
        // as malformed as a short number, so it throws like ReadUInt32 does.
        public new byte[] ReadBytes(int count)
        {
            var bytes = base.ReadBytes(count);
            if (bytes.Length < count)
                throw new EndOfStreamException();
            return bytes;
        }

        public string ReadString(int fixedLength)
        {
            return Encoding.ASCII.GetString(this.ReadBytes(fixedLength)).TrimEnd('\0');
        }

        // A count, then each string as a one-byte length and that many bytes in the client's
        // code page. Latin1 maps every byte to one char and back, so relayed text keeps its bytes.
        public List<string> ReadStrings()
        {
            var strings = new List<string>();
            var amount = base.ReadByte();
            for (int i = 0; i < amount; i++)
            {
                int length = base.ReadByte();
                strings.Add(Encoding.Latin1.GetString(this.ReadBytes(length)).TrimEnd('\0'));
            }
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
