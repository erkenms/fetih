namespace Fetih.Game.Npcs
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Game.World;

    public sealed record Destination
    {
        [JsonPropertyName("name")] public string Name { get; init; }
        [JsonPropertyName("map")] public uint MapID { get; init; }
        [JsonPropertyName("x")] public ushort X { get; init; }
        [JsonPropertyName("y")] public ushort Y { get; init; }
    }

    public sealed record Teleporter
    {
        [JsonPropertyName("id")] public uint NpcID { get; init; }
        [JsonPropertyName("fare")] public uint Fare { get; init; }
        [JsonPropertyName("face")] public byte Face { get; init; }
        [JsonPropertyName("text")] public List<string> Text { get; init; }
        [JsonPropertyName("destinations")] public List<Destination> Destinations { get; init; }
        [JsonPropertyName("close")] public string Close { get; init; }
    }

    public sealed class Teleporters
    {
        private readonly Dictionary<uint, Teleporter> ByNpc;

        private sealed record TeleporterFile(List<Teleporter> Teleporters);

        public static List<Teleporter> Load(string path)
        {
            var teleporters = DataFiles.Read<TeleporterFile>(path).Teleporters ?? new List<Teleporter>();
            var repeated = teleporters.GroupBy(t => t.NpcID).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repeated.Count > 0)
                throw new InvalidDataException($"{path} lists these NPC IDs more than once: {string.Join(", ", repeated)}");
            var bad = teleporters.Where(t => t.Destinations == null || t.Destinations.Count == 0
                || t.Destinations.Count >= MsgTaskDialog.NoOption).Select(t => t.NpcID).ToList();
            if (bad.Count > 0)
                throw new InvalidDataException($"{path} has no or too many destinations for NPCs {string.Join(", ", bad)}");
            return teleporters;
        }

        public Teleporters(IEnumerable<Teleporter> teleporters)
        {
            this.ByNpc = teleporters.ToDictionary(t => t.NpcID);
        }

        public int Count => this.ByNpc.Count;

        public IEnumerable<Teleporter> All => this.ByNpc.Values;

        public Teleporter Find(uint npcID) => this.ByNpc.TryGetValue(npcID, out var t) ? t : null;

        public static async Task TalkAsync(NpcTalk talk, Teleporter teleporter)
        {
            if (talk.Option == 0)
            {
                foreach (var line in teleporter.Text ?? new List<string>())
                    talk.Text(line);
                for (int i = 0; i < teleporter.Destinations.Count; i++)
                    talk.Choice(teleporter.Destinations[i].Name, (byte)(i + 1));
                await talk.Choice(teleporter.Close ?? "Just passing by.", MsgTaskDialog.NoOption).Face(teleporter.Face).ShowAsync();
                return;
            }

            if (talk.Option > teleporter.Destinations.Count)
                return;
            var to = teleporter.Destinations[talk.Option - 1];
            var client = talk.Client;
            if (client.Character.Silver < teleporter.Fare)
            {
                await talk
                    .Text($"Sorry, you do not have {teleporter.Fare} silver.")
                    .Choice("I see.", MsgTaskDialog.NoOption)
                    .Face(teleporter.Face)
                    .ShowAsync();
                return;
            }

            if (await Movement.TeleportAsync(client, to.MapID, to.X, to.Y) && teleporter.Fare > 0)
                await Progress.ChangeSilverAsync(client, -teleporter.Fare, "teleporter");
        }
    }
}
