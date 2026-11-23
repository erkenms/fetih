namespace Comet.Game.Arena
{
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // The Honor Points each qualifier rank earns when a season (a day) ends, read from the
    // client's ini/HonorRewards.ini: "RankAmount=1000", then "rank=honor/other" lines. The
    // client's ranking window shows the first number for each rank; it never reads the second.
    public sealed class HonorRewards
    {
        private readonly uint[] ByRank;

        public HonorRewards(uint[] byRank) => this.ByRank = byRank;

        public int Count => this.ByRank.Length;

        // 0 past the last rewarded rank
        public uint For(int rank) => rank >= 1 && rank <= this.ByRank.Length ? this.ByRank[rank - 1] : 0;

        public static HonorRewards Load(string clientDirectory)
        {
            var path = Path.Combine(clientDirectory, "ini", "HonorRewards.ini");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No ini/HonorRewards.ini in {clientDirectory}", path);
            return Parse(File.ReadAllText(path, Encoding.Latin1));
        }

        public static HonorRewards Parse(string text)
        {
            var byRank = new Dictionary<int, uint>();
            int amount = 0;
            foreach (var raw in text.Split('\n'))
            {
                int equals = raw.IndexOf('=');
                if (equals < 0)
                    continue;
                var key = raw.Substring(0, equals).Trim();
                var value = raw.Substring(equals + 1).Trim();
                if (key == "RankAmount")
                {
                    amount = int.Parse(value, CultureInfo.InvariantCulture);
                    continue;
                }
                int slash = value.IndexOf('/');
                if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var rank) && rank >= 1 &&
                    uint.TryParse(slash < 0 ? value : value.Substring(0, slash), NumberStyles.None,
                        CultureInfo.InvariantCulture, out var honor))
                    byRank[rank] = honor;
            }

            var table = new uint[amount];
            for (int rank = 1; rank <= amount; rank++)
                if (!byRank.TryGetValue(rank, out table[rank - 1]))
                    throw new InvalidDataException($"HonorRewards.ini has no rank {rank} of {amount}");
            return new HonorRewards(table);
        }
    }
}
