namespace Comet.Game.Npcs
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Comet.Game.Packets;

    // An NPC that only says something; the player's one answer closes the dialog
    public sealed record Talker
    {
        [JsonPropertyName("id")] public uint NpcID { get; init; }
        [JsonPropertyName("face")] public byte Face { get; init; }
        [JsonPropertyName("text")] public List<string> Text { get; init; }
        [JsonPropertyName("reply")] public string Reply { get; init; }
    }

    public sealed class Talkers
    {
        private readonly Dictionary<uint, Talker> ByNpc;

        private sealed record TalkerFile(List<Talker> Talkers);

        public static List<Talker> Load(string path)
        {
            var talkers = DataFiles.Read<TalkerFile>(path).Talkers ?? new List<Talker>();
            var repeated = talkers.GroupBy(t => t.NpcID).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repeated.Count > 0)
                throw new InvalidDataException($"{path} lists these NPC IDs more than once: {string.Join(", ", repeated)}");
            var bad = talkers.Where(t => t.Text == null || t.Text.Count == 0 || string.IsNullOrEmpty(t.Reply))
                .Select(t => t.NpcID).ToList();
            if (bad.Count > 0)
                throw new InvalidDataException($"{path} has no text or reply for NPCs {string.Join(", ", bad)}");
            return talkers;
        }

        public Talkers(IEnumerable<Talker> talkers)
        {
            this.ByNpc = talkers.ToDictionary(t => t.NpcID);
        }

        public int Count => this.ByNpc.Count;

        public Talker Find(uint npcID) => this.ByNpc.TryGetValue(npcID, out var t) ? t : null;

        public static Task TalkAsync(NpcTalk talk, Talker said)
        {
            foreach (var line in said.Text)
                talk.Text(line);
            return talk.Choice(said.Reply, MsgTaskDialog.NoOption).Face(said.Face).ShowAsync();
        }
    }
}
