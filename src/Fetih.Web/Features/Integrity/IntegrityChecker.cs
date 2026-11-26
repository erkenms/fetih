namespace Fetih.Web.Features.Integrity;

using Fetih.Game.Combat;
using Fetih.Game.DataEditing;
using Fetih.Game.Items;
using Fetih.Game.Npcs;
using Fetih.Game.States;
using Fetih.Game.World;

// Every DataSet check over every data file, each problem with a link to the editor that fixes it
public static class IntegrityChecker
{
    public static IntegrityReportResponse Run(WebConfiguration config, ClientFiles client) =>
        Run(client.CreateDataSet(StudioData.ResolveDataDirectory(config)));

    public static IntegrityReportResponse Run(DataSet set)
    {
        var problems = new List<IntegrityProblem>();
        var files = new List<IntegrityFileSummary>();
        if (Directory.Exists(set.Directory))
        {
            foreach (var kind in DataFileKinds.All)
            {
                if (!File.Exists(Path.Combine(set.Directory, kind.FileName)))
                    continue;

                var found = set.Validate(kind.FileName);
                DataFile? file = null;
                try { file = set.File(kind.FileName); }
                catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or IOException) { }

                foreach (var p in found)
                {
                    var (entity, editorPath, editorKind) = Link(kind, file, p.Entry);
                    problems.Add(new IntegrityProblem(kind.FileName, Category(p.Message), Level(p.Level),
                        p.Entry, p.Line, entity, p.Message, editorPath, editorKind));
                }

                files.Add(new IntegrityFileSummary(kind.FileName,
                    found.Count(p => p.Level == DataProblemLevel.Error),
                    found.Count(p => p.Level == DataProblemLevel.Warning),
                    file?.Count ?? 0));
            }
        }

        return new IntegrityReportResponse(
            set.Directory,
            set.ChecksClient,
            ErrorCount: problems.Count(p => p.Level == "error"),
            WarningCount: problems.Count(p => p.Level == "warning"),
            CheckedFiles: files.Count,
            Files: [.. files],
            Problems: [.. problems]);
    }

    private static string Level(DataProblemLevel level) => level == DataProblemLevel.Warning ? "warning" : "error";

    // Groups DataSet's messages for the page's category filter
    internal static string Category(string message)
    {
        bool Has(string text) => message.Contains(text, StringComparison.OrdinalIgnoreCase);
        if (Has("isn't in monsters.json"))
            return "missing_monster";
        if (Has("isn't in npcs.json"))
            return "missing_npc";
        if (Has("GameMap.dat") || Has("can't be read"))
            return "missing_map";
        if (Has("itemtype.dat"))
            return "missing_item";
        if (Has("isn't walkable"))
            return "unwalkable_cell";
        if (Has("is outside map"))
            return "out_of_bounds";
        if (Has("has no portal"))
            return "missing_portal";
        if (Has("is already used") || Has("is also in") || Has("is also used"))
            return "duplicate_id";
        if (Has("JSON") || Has("parse"))
            return "parse_error";
        return "invalid_value";
    }

    private static (string? Entity, string EditorPath, string EditorKind) Link(DataFileKind kind, DataFile? file, int? entry)
    {
        if (file != null && entry is int i && i >= 0 && i < file.Count)
        {
            switch (file)
            {
                case DataFile<Generator> f:
                    var s = f[i];
                    return ($"Spawn #{s.ID}", $"/admin/maps/{s.MapID}?x={s.X}&y={s.Y}", "map");
                case DataFile<BossSpawn> f:
                    var b = f[i];
                    return (string.IsNullOrWhiteSpace(b.ID) ? $"Boss #{i}" : b.ID, $"/admin/maps/{b.MapID}?x={b.X}&y={b.Y}", "map");
                case DataFile<Npc> f when kind == DataFileKinds.Goals:
                    return ($"Goal {f[i].NpcID}", "/admin/data?tab=goals", "goals");
                case DataFile<Npc> f:
                    var n = f[i];
                    return ($"NPC {n.NpcID}", $"/admin/maps/{n.MapID}?x={n.X}&y={n.Y}", "map");
                case DataFile<Portal> f:
                    var p = f[i];
                    return ($"Portal {p.MapID}#{p.PortalIndex}", $"/admin/maps/{p.MapID}", "map");
                case DataFile<MapType> f:
                    var m = f[i];
                    return (string.IsNullOrWhiteSpace(m.Name) ? $"Map {m.MapID}" : $"{m.Name} ({m.MapID})", $"/admin/maps/{m.MapID}", "map");
                case DataFile<MonsterType> f:
                    var mon = f[i];
                    return (string.IsNullOrWhiteSpace(mon.Name) ? $"Monster {mon.ID}" : $"{mon.Name} ({mon.ID})", "/admin/monsters", "monsters");
                case DataFile<LevelExp> f:
                    return ($"Level {f[i].Level}", "/admin/data?tab=levels", "levels");
                case DataFile<Talker> f:
                    return ($"NPC {f[i].NpcID}", "/admin/data?tab=talkers", "talkers");
                case DataFile<Teleporter> f:
                    return ($"NPC {f[i].NpcID}", "/admin/data?tab=teleporters", "teleporters");
                case DataFile<MonsterDrops> f:
                    return ($"Monster {f[i].MonsterID}", "/admin/changes", "changes");
            }
        }

        return kind.FileName switch
        {
            "spawns.json" or "bosses.json" or "npcs.json" or "portals.json" or "maps.json" => (null, "/admin/maps", "map"),
            "monsters.json" => (null, "/admin/monsters", "monsters"),
            "levels.json" => (null, "/admin/data?tab=levels", "levels"),
            "goals.json" => (null, "/admin/data?tab=goals", "goals"),
            "teleporters.json" => (null, "/admin/data?tab=teleporters", "teleporters"),
            "talkers.json" => (null, "/admin/data?tab=talkers", "talkers"),
            _ => (null, "/admin/changes", "changes"),
        };
    }
}
