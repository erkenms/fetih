namespace Fetih.Game.Tests.DataEditing
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Fetih.Game.Combat;
    using Fetih.Game.DataEditing;
    using Fetih.Game.Npcs;
    using Fetih.Game.World;
    using Xunit;

    public class DataFileTests
    {
        public static readonly string SourceData = FindSourceData();

        public static IEnumerable<object[]> Files() => DataFileKinds.All.Select(k => new object[] { k.FileName });

        [Theory]
        [MemberData(nameof(Files))]
        public void RewritingEveryEntryAsReadKeepsTheFileByteForByte(string name)
        {
            string path = Path.Combine(SourceData, name);
            var bytes = File.ReadAllBytes(path);
            var file = DataFileKinds.Find(name).Load(path);
            Assert.True(file.Count > 0);

            for (int i = 0; i < file.Count; i++)
                file.SetJson(i, file.GetJson(i));

            Assert.False(file.IsChanged);
            Assert.Equal(bytes, file.Bytes());
            Assert.Equal(DataFile.HashOf(bytes), file.Hash);
        }

        [Fact]
        public void MovingAnNpcChangesOneLine()
        {
            var file = Load<Npc>(DataFileKinds.Npcs);
            int index = file.Count / 2;
            var npc = file[index];
            file[index] = With(npc, x: (ushort)(npc.X + 3), y: (ushort)(npc.Y - 1));

            var (removed, added) = Diff(file.Original, file.Text);
            Assert.Single(removed);
            Assert.Single(added);
            Assert.Equal(removed[0].Replace($"\"x\": {npc.X}", $"\"x\": {npc.X + 3}").Replace($"\"y\": {npc.Y}", $"\"y\": {npc.Y - 1}"), added[0]);
            Assert.Equal(npc.X + 3, file[index].X);
        }

        [Fact]
        public void ChangingAMonsterKeepsItsCompactStyle()
        {
            var file = Load<MonsterType>(DataFileKinds.Monsters);
            var json = file.GetJson(0);
            json["life"] = 1234;
            file.SetJson(0, json);

            var (removed, added) = Diff(file.Original, file.Text);
            Assert.Equal(new[] { removed.Single().Replace("\"life\": 33", "\"life\": 1234") }, added);
            Assert.DoesNotContain("magicHitRate", added[0]);
        }

        [Fact]
        public void ChangingATalkerReplyKeepsItsComment()
        {
            var file = Load<Talker>(DataFileKinds.Talkers);
            file[0] = file[0] with { Reply = "Dream on." };

            var (removed, added) = Diff(file.Original, file.Text);
            Assert.Equal("      \"reply\": \"In your dreams.\",", removed.Single());
            Assert.Equal("      \"reply\": \"Dream on.\",", added.Single());
            Assert.Contains("// GhoulKong, Desert City", file.Text);
        }

        [Fact]
        public void AddingAndRemovingNpcsTouchesOnlyTheirLines()
        {
            var file = Load<Npc>(DataFileKinds.Npcs);
            string original = file.Text.Split('\n')[file.LineOf(3) - 1];
            uint id = file[3].NpcID;
            file.Insert(3, With(file[3], id: 999999));

            var (removed, added) = Diff(file.Original, file.Text);
            Assert.Empty(removed);
            Assert.Equal(original.Replace($"\"id\": {id},", "\"id\": 999999,"), added.Single());

            file.RemoveAt(3);
            Assert.Equal(file.Original, file.Text);
        }

        [Fact]
        public void AppendingToTheEndMovesTheComma()
        {
            var file = Load<Npc>(DataFileKinds.Npcs);
            var last = file[file.Count - 1];
            file.Add(With(last, id: 999999));
            var (removed, added) = Diff(file.Original, file.Text);
            Assert.Single(removed);
            Assert.Equal(removed[0] + ",", added[0]);
            Assert.Equal(2, added.Count);
            Assert.Equal(999999u, file[file.Count - 1].NpcID);

            file.RemoveAt(file.Count - 1);
            Assert.Equal(file.Original, file.Text);
        }

        [Fact]
        public void RemovingAndAddingOptionalMembersOfAMap()
        {
            var file = Load<MapType>(DataFileKinds.Maps);
            int dc = file.IndexOf(m => m.MapID == 1000);
            var json = file.GetJson(dc).AsObject();
            json.Remove("aliases");
            json["ridePlus"] = 5;
            json["reborn"] = 1002;
            file.SetJson(dc, json);

            var (removed, added) = Diff(file.Original, file.Text);
            Assert.Equal(removed.Single()
                    .Replace(", \"aliases\": [\"dc\"]", "")
                    .Replace("\"reborn\": 1000", "\"reborn\": 1002")
                    .Replace("\"ridePlus\": 4", "\"ridePlus\": 5"),
                added.Single());
            Assert.EndsWith("// DesertCity", added[0]);

            var withoutScroll = file.IndexOf(m => m.ScrollTypeID == null && m.Portal != null);
            var map = file.GetJson(withoutScroll);
            map["scroll"] = 1060099;
            file.SetJson(withoutScroll, map);
            Assert.Contains(", \"scroll\": 1060099 }", file.Text);
        }

        [Fact]
        public void TeleporterDestinationsAreLinesOfTheirOwn()
        {
            var file = Load<Teleporter>(DataFileKinds.Teleporters);
            var teleporter = file[0];
            var more = teleporter.Destinations.Append(new Destination { Name = "Market", MapID = 1036, X = 211, Y = 196 }).ToList();
            file[0] = teleporter with { Destinations = more };

            var (removed, added) = Diff(file.Original, file.Text);
            var lines = file.Original.Split('\n');
            var lastDestination = lines.First(l => l.Contains(teleporter.Destinations[^1].Name));
            if (lastDestination.TrimEnd().EndsWith(','))
                Assert.Empty(removed);
            else
                Assert.Equal(lastDestination, removed.Single());
            Assert.Equal(lastDestination.Length - lastDestination.TrimStart().Length,
                added[^1].Length - added[^1].TrimStart().Length);
            Assert.Contains("{ \"name\": \"Market\", \"map\": 1036, \"x\": 211, \"y\": 196 }", added[^1]);

            file[0] = teleporter;
            Assert.Equal(file.Original, file.Text);
        }

        [Fact]
        public void NewMultiLineEntriesFollowTheirNeighbours()
        {
            var file = Load<Teleporter>(DataFileKinds.Teleporters);
            var copy = file[1] with { NpcID = 999999, Fare = 7 };
            file.Insert(1, copy);

            Assert.Equal(7u, file[1].Fare);
            Assert.Equal(file[2].Destinations, file[1].Destinations);
            var (removed, added) = Diff(file.Original, file.Text);
            Assert.Empty(removed);
            var neighbour = file.Text.Split('\n')[(file.LineOf(2) - 1)..(file.LineOf(3) - 1)];
            var inserted = file.Text.Split('\n')[(file.LineOf(1) - 1)..(file.LineOf(2) - 1)];
            Assert.Equal(neighbour.Length, inserted.Length);
            Assert.Equal(neighbour.Length, added.Count);
            Assert.Equal("    {", inserted[0]);
            Assert.Equal("    },", inserted[^1]);
            Assert.Contains(inserted, l => l.StartsWith("        { \"name\": "));

            file.RemoveAt(1);
            Assert.Equal(file.Original, file.Text);
        }

        [Fact]
        public void DropTableEntriesCanBeRemoved()
        {
            var file = Load<MonsterDrops>(DataFileKinds.Drops);
            var drops = file[0];
            var fewer = new MonsterDrops { MonsterID = drops.MonsterID, Entries = drops.Entries.Skip(1).ToList() };
            file[0] = fewer;

            var (removed, added) = Diff(file.Original, file.Text);
            Assert.Single(removed);
            Assert.Empty(added);
            Assert.Equal(drops.Entries.Count - 1, file[0].Entries.Count);
        }

        [Fact]
        public void KeepsCarriageReturnsAndUnknownMembers()
        {
            string text = "// header\r\n{\r\n  \"npcs\": [\r\n    { \"id\": 1, \"note\": \"kept\", \"map\": 1002, \"x\": 1, \"y\": 2 }\r\n  ]\r\n}\r\n";
            var file = DataFile<Npc>.Parse(text, "npcs");
            file[0] = With(file[0], x: 5);
            file.Add(With(file[0], id: 2));

            Assert.Equal("// header\r\n{\r\n  \"npcs\": [\r\n" +
                "    { \"id\": 1, \"note\": \"kept\", \"map\": 1002, \"x\": 5, \"y\": 2 },\r\n" +
                "    { \"id\": 2, \"map\": 1002, \"x\": 5, \"y\": 2 }\r\n" +
                "  ]\r\n}\r\n", file.Text);
        }

        [Fact]
        public void AnEmptyListTakesItsFirstEntry()
        {
            var file = DataFile<Portal>.Parse("{ \"portals\": [] }", "portals");
            file.Add(new Portal { MapID = 1, PortalIndex = 2, TargetMapID = 3, TargetX = 4, TargetY = 5 });
            Assert.Equal("{ \"portals\": [{ \"map\": 1, \"portal\": 2, \"toMap\": 3, \"toX\": 4, \"toY\": 5 }] }", file.Text);
            Assert.Equal(3u, file[0].TargetMapID);
        }

        [Fact]
        public void SavesAtomicallyAndRefusesToOverwriteSomeoneElsesChange()
        {
            string dir = Directory.CreateTempSubdirectory("fetih-data-").FullName;
            try
            {
                string path = Path.Combine(dir, "portals.json");
                File.Copy(Path.Combine(SourceData, "portals.json"), path);
                var file = DataFile<Portal>.Load(path, "portals");
                var other = DataFile<Portal>.Load(path, "portals");
                Assert.Equal(file.Hash, other.Hash);

                var portal = file[0];
                file[0] = new Portal { MapID = portal.MapID, PortalIndex = portal.PortalIndex, TargetMapID = portal.TargetMapID, TargetX = 1, TargetY = 2 };
                file.Save();
                Assert.Equal(file.Text, File.ReadAllText(path));
                Assert.Equal(DataFile.HashOf(File.ReadAllBytes(path)), file.Hash);
                Assert.False(file.IsChanged);

                other.RemoveAt(0);
                Assert.Throws<DataFileConflictException>(() => other.Save());
                Assert.Throws<DataFileConflictException>(() => DataFile<Portal>.Load(path, "portals").EnsureHash(other.Hash));
                Assert.Equal(file.Text, File.ReadAllText(path));
                Assert.Equal(new[] { "portals.json" }, Directory.GetFiles(dir).Select(Path.GetFileName));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static DataFile<T> Load<T>(DataFileKind kind) where T : class =>
            DataFile<T>.Load(Path.Combine(SourceData, kind.FileName), kind.ArrayKey);

        private static Npc With(Npc npc, uint? id = null, ushort? x = null, ushort? y = null) => new Npc
        {
            NpcID = id ?? npc.NpcID,
            Lookface = npc.Lookface,
            Role = npc.Role,
            MapID = npc.MapID,
            X = x ?? npc.X,
            Y = y ?? npc.Y,
            Life = npc.Life,
            Level = npc.Level,
        };

        // Lines only in before, and lines only in after, by a plain longest-common-subsequence diff
        private static (List<string> Removed, List<string> Added) Diff(string before, string after)
        {
            var a = before.Split('\n');
            var b = after.Split('\n');
            var lcs = new int[a.Length + 1, b.Length + 1];
            for (int i = a.Length - 1; i >= 0; i--)
                for (int j = b.Length - 1; j >= 0; j--)
                    lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            var removed = new List<string>();
            var added = new List<string>();
            int x = 0, y = 0;
            while (x < a.Length || y < b.Length)
            {
                if (x < a.Length && y < b.Length && a[x] == b[y])
                {
                    x++;
                    y++;
                }
                else if (y < b.Length && (x == a.Length || lcs[x, y + 1] >= lcs[x + 1, y]))
                    added.Add(b[y++]);
                else
                    removed.Add(a[x++]);
            }

            return (removed, added);
        }

        private static string FindSourceData()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var data = Path.Combine(dir.FullName, "src", "Fetih.Game", "Data");
                if (Directory.Exists(data))
                    return data;
            }

            throw new DirectoryNotFoundException("No src/Fetih.Game/Data above the test folder");
        }
    }
}
