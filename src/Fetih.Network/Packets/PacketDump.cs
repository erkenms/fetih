namespace Fetih.Network.Packets
{
    using System;
    using System.Linq;
    using System.Text;

    public static class PacketDump
    {
        public static string Hex(ReadOnlySpan<byte> data)
        {
            var text = new StringBuilder();
            for (int l = 0; l < data.Length; l += 16) 
            {
                text.AppendFormat("{0:X4}:", l);
                for (int i = l; i < l + 16; i++) 
                {
                    text.Append(i % 8 == 0 ? "  " : " ");
                    text.Append(i >= data.Length ? "  " : 
                        string.Format("{0:X2}", data[i]));
                }

                int v = l + 16 >= data.Length ? data.Length : l + 16;
                text.Append("  | ");
                for (int i = l; i < v; i++) 
                    text.Append(data[i] < 32 || data[i] > 126 ? '.' : (char)data[i]);
                text.Append(" |\n");
            }

            return text.ToString();
        }
    }
}
