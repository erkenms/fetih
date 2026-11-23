namespace Comet.Game.Magic
{
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Comet.Game.Items;

    // What a rebirth does to a character's spells, as the client's ini/magictypeop.dat has it.
    // Decrypted, one comma-separated line per operation: id, the rebirth (1 or 2, 0 for none),
    // the class before, the class after, the operation, then spell types padded with 0.
    public sealed class MagicTypeOpTable
    {
        public enum Operation : byte
        {
            Forget = 0,

            // Kept at level 0; Casting levels it straight back up from half the old level
            Restart = 2,
            FirstLife = 4,
            Pure = 5,

            // What the client's rebirth panel shows the new life will have
            Preview = 6,
            Learn = 7,
        }

        private readonly Dictionary<(byte Rebirth, byte From, byte To, Operation Op), ushort[]> Ops;

        public MagicTypeOpTable(IEnumerable<(byte Rebirth, byte From, byte To, Operation Op, ushort[] Magics)> ops)
        {
            this.Ops = new Dictionary<(byte, byte, byte, Operation), ushort[]>();
            // The client has Monk to Monk restarts on two lines
            foreach (var op in ops)
            {
                var key = (op.Rebirth, op.From, op.To, op.Op);
                this.Ops[key] = this.Ops.TryGetValue(key, out var before) ? before.Union(op.Magics).ToArray() : op.Magics;
            }
        }

        public int Count => this.Ops.Count;

        public IReadOnlyList<ushort> Of(byte rebirth, byte from, byte to, Operation op) =>
            this.Ops.TryGetValue((rebirth, from, to, op), out var magics) ? magics : System.Array.Empty<ushort>();

        public static MagicTypeOpTable Load(string clientDirectory)
        {
            var path = Path.Combine(clientDirectory, "ini", "magictypeop.dat");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No ini/magictypeop.dat in {clientDirectory}", path);
            var data = File.ReadAllBytes(path);
            TqFileCipher.Decrypt(data);
            return Parse(data);
        }

        public static MagicTypeOpTable Parse(byte[] decrypted)
        {
            var ops = new List<(byte, byte, byte, Operation, ushort[])>();
            foreach (var line in Encoding.Latin1.GetString(decrypted).Split('\n'))
            {
                var fields = line.TrimEnd('\r').Split(',');
                if (fields.Length < 5 ||
                    !byte.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out byte rebirth) ||
                    !byte.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out byte from) ||
                    !byte.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out byte to) ||
                    !byte.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out byte op))
                    continue;
                var magics = fields.Skip(5)
                    .Select(f => ushort.TryParse(f, NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : (ushort)0)
                    .Where(m => m != 0)
                    .ToArray();
                ops.Add((rebirth, from, to, (Operation)op, magics));
            }
            return new MagicTypeOpTable(ops);
        }
    }
}
