namespace Fetih.Game.Tests.World
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Game.Combat;
    using Fetih.Game.Items;
    using Fetih.Game.World;
    using Xunit;

    public class BossScheduleTests
    {
        private static Monster NewBoss(uint monsterTypeID = 3587, string name = "TeratoDragon")
        {
            var type = new MonsterType { ID = monsterTypeID, Name = name, Life = 1000, Level = 140, Boss = true };
            var generator = new Generator { ID = 0, MapID = 1002, X = 100, Y = 100, Amount = 1, RespawnSeconds = -1, MonsterID = monsterTypeID };
            return new Monster(Monster.FirstID, type, generator, null) { CorpseRemoved = true };
        }

        [Fact]
        public async Task DailyBossSpawnsAtScheduledTimeOnceAndSkipsWhileAlive()
        {
            var announcements = new List<string>();
            var schedule = new BossSchedule(
                (m, _, _) => { m.Revive(); return Task.FromResult(true); },
                text => { announcements.Add(text); return Task.CompletedTask; },
                utc => utc);

            var monster = NewBoss();
            var start = new DateTime(2026, 6, 1, 7, 59, 0, DateTimeKind.Utc);
            schedule.AddEntry(new BossSpawn
            {
                ID = "dragon",
                MonsterID = 3587,
                MapID = 1002,
                X = 100,
                Y = 100,
                DailyTimes = new[] { TimeSpan.FromHours(8), TimeSpan.FromHours(20) },
                Broadcast = "Dragon is here!",
            }, monster, start);

            await schedule.TickAsync(new DateTime(2026, 6, 1, 7, 59, 59, DateTimeKind.Utc), new Random(1));
            Assert.False(monster.Alive);
            Assert.Empty(announcements);

            await schedule.TickAsync(new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), new Random(1));
            Assert.True(monster.Alive);
            Assert.Single(announcements, "Dragon is here!");

            // Still alive at the evening slot: no duplicate spawn or broadcast
            await schedule.TickAsync(new DateTime(2026, 6, 1, 20, 0, 0, DateTimeKind.Utc), new Random(1));
            Assert.Single(announcements);

            // Killed at 20:05; next day's 08:00 brings it back across midnight
            monster.Damage(1000, 1, new DateTime(2026, 6, 1, 20, 5, 0, DateTimeKind.Utc));
            Assert.False(monster.Alive);
            await schedule.TickAsync(new DateTime(2026, 6, 2, 7, 59, 0, DateTimeKind.Utc), new Random(1));
            Assert.False(monster.Alive);
            await schedule.TickAsync(new DateTime(2026, 6, 2, 8, 0, 1, DateTimeKind.Utc), new Random(1));
            Assert.True(monster.Alive);
            Assert.Equal(2, announcements.Count);
        }

        [Fact]
        public async Task IntervalBossSpawnsOnIntervalAndWaitsAfterDeath()
        {
            var announcements = new List<string>();
            var schedule = new BossSchedule(
                (m, _, _) => { m.Revive(); return Task.FromResult(true); },
                text => { announcements.Add(text); return Task.CompletedTask; },
                utc => utc);

            var monster = NewBoss(3130, "GanodermaL72");
            var start = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
            schedule.AddEntry(new BossSpawn
            {
                ID = "ganoderma",
                MonsterID = 3130,
                MapID = 1011,
                X = 72,
                Y = 364,
                IntervalSeconds = 60,
                Broadcast = "Ganoderma spawned!",
            }, monster, start);

            await schedule.TickAsync(start.AddSeconds(59), new Random(1));
            Assert.False(monster.Alive);

            await schedule.TickAsync(start.AddSeconds(60), new Random(1));
            Assert.True(monster.Alive);
            Assert.Single(announcements);

            var killedAt = start.AddSeconds(75);
            monster.Damage(1000, 1, killedAt);
            Assert.False(monster.Alive);

            await schedule.TickAsync(killedAt.AddSeconds(59), new Random(1));
            Assert.False(monster.Alive);

            await schedule.TickAsync(killedAt.AddSeconds(60), new Random(1));
            Assert.True(monster.Alive);
            Assert.Equal(2, announcements.Count);
        }

        [Fact]
        public async Task ManualSpawnEnforcesOneAliveAtATime()
        {
            var announcements = new List<string>();
            var schedule = new BossSchedule(
                (m, _, _) => { m.Revive(); return Task.FromResult(true); },
                text => { announcements.Add(text); return Task.CompletedTask; },
                utc => utc);

            var monster = NewBoss();
            var start = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
            schedule.AddEntry(new BossSpawn
            {
                ID = "dragon",
                MonsterID = 3587,
                MapID = 1002,
                DailyTimes = new[] { TimeSpan.FromHours(8) },
                Broadcast = "Dragon!",
            }, monster, start);

            Assert.Equal(BossSpawnResult.NotFound, await schedule.SpawnAsync("missing", start, new Random(1)));
            Assert.Equal(BossSpawnResult.Spawned, await schedule.SpawnAsync("dragon", start, new Random(1)));
            Assert.Equal(BossSpawnResult.AlreadyAlive, await schedule.SpawnAsync("dragon", start, new Random(1)));
            Assert.Single(announcements);
        }

        [Fact]
        public void DropTableOverridesFlatRatesWhileOtherMonstersKeepFlatRates()
        {
            var items = new ItemTypeTable(new[]
            {
                new ItemType { ID = 1088000, Name = "DragonBall" },
                new ItemType { ID = 700013, Name = "SuperDragonGem" },
                new ItemType { ID = 1000000, Name = "Stancher" },
            });
            var table = new DropTable(new[]
            {
                new MonsterDrops
                {
                    MonsterID = 3587,
                    Entries = new List<DropEntry>
                    {
                        new DropEntry { ItemID = 1088000, Chance = 100, Count = 2 },
                        new DropEntry { ItemID = 700013, Chance = 50, Count = 1 },
                    },
                },
            });

            var boss = new MonsterType { ID = 3587, Name = "TeratoDragon", Level = 140, Life = 50_000_000, Boss = true };
            var random = new Random(42);
            var bossRolls = Enumerable.Range(0, 200).Select(_ => Loot.Roll(boss, items, random, table)).ToList();
            Assert.All(bossRolls, r => Assert.Equal(2, r.Count(d => d.TypeID == 1088000)));
            Assert.All(bossRolls, r => Assert.DoesNotContain(r, d => d.Silver > 0));
            Assert.InRange(bossRolls.Count(r => r.Any(d => d.TypeID == 700013)), 70, 130);

            var pheasant = new MonsterType { ID = 1, Name = "Pheasant", Level = 1, Life = 33, DropMoney = 50, DropHp = 1000000 };
            var plainRolls = Enumerable.Range(0, 500).SelectMany(_ => Loot.Roll(pheasant, items, random, table)).ToList();
            Assert.Contains(plainRolls, d => d.Silver > 0);
            Assert.Contains(plainRolls, d => d.TypeID == 1000000);
        }

        [Fact]
        public void DataBossesAndDropsLoadCleanly()
        {
            var monsters = new MonsterTypeTable(MonsterTypeTable.Load(DataFiles.PathOf("monsters.json")));
            var bosses = BossSpawn.Load(DataFiles.PathOf("bosses.json"), monsters);
            Assert.True(bosses.Count >= 6);
            Assert.Contains(bosses, b => b.ID == "terato-dragon" && b.MonsterID == 3587);
            Assert.Contains(bosses, b => b.ID == "thrilling-spook" && b.MonsterID == 3588);
            Assert.Contains(bosses, b => b.ID == "snow-banshee" && b.MonsterID == 3589);
            Assert.Contains(bosses, b => b.ID == "sword-master" && b.MonsterID == 3590);
            Assert.Contains(bosses, b => b.ID == "ganoderma" && b.MonsterID == 3130);
            Assert.Contains(bosses, b => b.ID == "titan" && b.MonsterID == 3134);

            var dropItems = new ItemTypeTable(new uint[]
            {
                1088000, 1088001, 720027, 720028, 723700, 721258,
                730002, 730003, 700001, 700003, 700011, 700012, 700013, 700021, 700071, 700072, 700073,
            }.Select(id => new ItemType { ID = id, Name = "Item" + id }));
            var drops = DropTable.Load(DataFiles.PathOf("drops.json"), monsters, dropItems);
            Assert.Equal(6, drops.Count);

            var temp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(temp, "{ \"bosses\": [{ \"id\": \"bad\", \"monster\": 3587, \"map\": 1002, \"times\": [\"25:00\"] }] }");
                Assert.Throws<InvalidDataException>(() => BossSpawn.Load(temp, monsters));

                File.WriteAllText(temp, "{ \"drops\": [{ \"monster\": 3587, \"entries\": [{ \"item\": 9999999, \"chance\": 50, \"count\": 1 }] }] }");
                Assert.Throws<InvalidDataException>(() => DropTable.Load(temp, monsters, dropItems));
            }
            finally
            {
                File.Delete(temp);
            }
        }
    }
}
