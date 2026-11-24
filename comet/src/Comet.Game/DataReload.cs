namespace Comet.Game
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Npcs;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Shared.Models;

    // Re-reads one Data/ file into the running world. Every file is read and checked first;
    // only when that passes are the tables swapped and players shown the difference.
    public static class DataReload
    {
        // Full paths of the files the configuration names; Data/ defaults when not set
        public static string SpawnsFile, DropsFile, BossesFile;

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        private static string Spawns => SpawnsFile ?? DataFiles.PathOf("spawns.json");
        private static string Drops => DropsFile ?? DataFiles.PathOf("drops.json");
        private static string Bosses => BossesFile ?? DataFiles.PathOf("bosses.json");

        // The names ReloadAsync takes
        public static IReadOnlyList<string> Files => new[]
        {
            "npcs.json", "goals.json", "portals.json", "maps.json", "teleporters.json", "talkers.json",
            "monsters.json", Path.GetFileName(Spawns), Path.GetFileName(Drops), "levels.json",
        };

        public static async Task<DataReloadResult> ReloadAsync(string file)
        {
            var result = new DataReloadResult { File = file };
            await Gate.WaitAsync();
            try
            {
                Func<Task<string>> apply = null;
                try { apply = Prepare(file ?? "", result.Warnings); }
                catch (Exception e) when (e is InvalidDataException || e is JsonException || e is IOException ||
                    e is UnauthorizedAccessException || e is ArgumentException)
                {
                    result.Errors.Add(e.Message);
                }

                if (apply == null)
                {
                    LogEvents.For(typeof(DataReload), LogEvents.DataReloadRejected).Warning(
                        "Didn't reload {File}: {Errors}", file, string.Join("; ", result.Errors));
                    return result;
                }

                result.Summary = await apply();
                result.Applied = true;
                LogEvents.For(typeof(DataReload), LogEvents.DataReloaded).Information(
                    "Reloaded {File}: {Summary}", file, result.Summary);
                return result;
            }
            finally { Gate.Release(); }
        }

        // Reads and checks the file, and returns what puts it into the world
        private static Func<Task<string>> Prepare(string file, List<string> warnings)
        {
            if (file.IndexOfAny(new[] { '/', '\\' }) >= 0 || !Files.Contains(file, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"{file} isn't a data file the server can reload; these are: {string.Join(", ", Files)}");

            if (Is(file, "npcs.json") || Is(file, "goals.json"))
            {
                var npcs = NpcTable.Load(DataFiles.PathOf("npcs.json"), DataFiles.PathOf("goals.json"));
                var placed = npcs.Where(n => Kernel.Maps.Exists(n.MapID)).ToList();
                foreach (var npc in npcs.Where(n => !Kernel.Maps.Exists(n.MapID)))
                    warnings.Add($"NPC {npc.NpcID} is on map {npc.MapID}, which the client doesn't have");
                var next = new NpcTable(placed);
                return () => Task.FromResult(ApplyNpcs(next));
            }

            if (Is(file, "portals.json"))
            {
                var portals = new PortalTable(PortalTable.Load(DataFiles.PathOf("portals.json")));
                return () => Swap(() => Kernel.Portals = portals, $"{portals.Count} portals");
            }

            if (Is(file, "maps.json"))
            {
                var maps = new MapTypes(MapTypes.Load(DataFiles.PathOf("maps.json")));
                return () => Swap(() => Kernel.MapTypes = maps,
                    $"{maps.Count} maps; players see new map flags when they next enter the map");
            }

            if (Is(file, "teleporters.json"))
            {
                var teleporters = Teleporters.Load(DataFiles.PathOf("teleporters.json"));
                foreach (var (npcID, to) in teleporters.SelectMany(t => t.Destinations.Select(d => (t.NpcID, d))))
                    if (!Kernel.Maps.Exists(to.MapID))
                        warnings.Add($"Teleporter {npcID} leads to map {to.MapID}, which the client doesn't have");
                var table = new Teleporters(teleporters);
                return () => Swap(() => Kernel.Teleporters = table, $"{table.Count} teleporters");
            }

            if (Is(file, "talkers.json"))
            {
                var table = new Talkers(Talkers.Load(DataFiles.PathOf("talkers.json")));
                return () => Swap(() => Kernel.Talkers = table, $"{table.Count} NPCs that only talk");
            }

            if (Is(file, "levels.json"))
            {
                var table = new ExperienceTable(ExperienceTable.Load(DataFiles.PathOf("levels.json")));
                return () => Swap(() => Kernel.Experience = table,
                    $"{table.Count} levels; the level cap is {table.MaxLevel}");
            }

            if (Is(file, "monsters.json"))
            {
                var types = new MonsterTypeTable(MonsterTypeTable.Load(DataFiles.PathOf("monsters.json")));
                // The other files name monsters by ID, so they must still find theirs
                Generator.Load(Spawns, types);
                Combat.DropTable.Load(Drops, types, Kernel.ItemTypes);
                BossSpawn.Load(Bosses, types);
                return () => OnMonsterLoop(async () =>
                {
                    var old = Kernel.MonsterTypes;
                    bool changed(uint id) => !old.TryGet(id, out var was) || !types.TryGet(id, out var now) ||
                        JsonSerializer.Serialize(was) != JsonSerializer.Serialize(now);
                    Kernel.MonsterTypes = types;
                    var (kept, removed, placed, _) = await Monsters.ReplaceSpawnsAsync(
                        Monsters.Generators, changed, DateTime.UtcNow, Random.Shared);
                    return $"{types.Count} monster types; {removed} monsters of changed types replaced by {placed}, " +
                        $"{kept} kept; scheduled bosses keep their old type until a restart";
                });
            }

            if (Is(file, Path.GetFileName(Spawns)))
            {
                var generators = Generator.Load(Spawns, Kernel.MonsterTypes);
                foreach (var generator in generators.Where(g => !Kernel.Maps.Exists(g.MapID)))
                    warnings.Add($"Spawn {generator.ID} is on map {generator.MapID}, which the client doesn't have");
                return () => OnMonsterLoop(async () =>
                {
                    var (kept, removed, placed, skipped) = await Monsters.ReplaceSpawnsAsync(
                        generators, _ => false, DateTime.UtcNow, Random.Shared);
                    return $"{generators.Count} spawns; {removed} monsters removed, {placed} placed, " +
                        $"{kept} kept, {skipped} spawns skipped";
                });
            }

            if (Is(file, Path.GetFileName(Drops)))
            {
                var drops = new Combat.DropTable(Combat.DropTable.Load(Drops, Kernel.MonsterTypes, Kernel.ItemTypes));
                return () => Swap(() => Kernel.Drops = drops, $"drop tables for {drops.Count} monsters");
            }

            throw new ArgumentException($"{file} can't be reloaded");
        }

        private static bool Is(string file, string name) => string.Equals(file, name, StringComparison.OrdinalIgnoreCase);

        private static Task<string> Swap(Action swap, string summary)
        {
            swap();
            return Task.FromResult(summary);
        }

        private static string ApplyNpcs(NpcTable next)
        {
            var (gone, shown) = Kernel.Npcs.Changes(next);
            Kernel.Npcs = next;
            if (gone.Count > 0 || shown.Count > 0)
                foreach (var client in Kernel.Players.Values)
                    Kernel.Post(client, () => RefreshNpcsAsync(client, gone, shown));
            return $"{next.Count} NPCs; {gone.Count} removed or changed, {shown.Count} added or changed";
        }

        private static async Task RefreshNpcsAsync(Client client, List<Npc> gone, List<Npc> shown)
        {
            var character = client.Character;
            if (character == null)
                return;
            foreach (var npc in gone)
                if (npc.MapID == character.MapID && GameMap<Client>.InView(character.X, character.Y, npc.X, npc.Y))
                    await client.SendAsync(MsgAction.RemoveEntity(npc.NpcID));
            foreach (var npc in shown)
                if (npc.MapID == character.MapID && GameMap<Client>.InView(character.X, character.Y, npc.X, npc.Y))
                    await Items.Vending.ShowNpcAsync(client, npc);
        }

        // Monsters are placed and moved by the monster loop only
        private static Task<string> OnMonsterLoop(Func<Task<string>> work)
        {
            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            MonsterAI.Post(async () =>
            {
                try { done.SetResult(await work()); }
                catch (Exception e) { done.SetException(e); }
            });
            return done.Task;
        }
    }
}
