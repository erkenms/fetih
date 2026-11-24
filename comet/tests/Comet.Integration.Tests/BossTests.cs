namespace Comet.Integration.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class BossTests : IntegrationTest
    {
        public BossTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task TimedBossSpawnsWithBroadcastAndDropsFromItsTableWithOwnerRules()
        {
            var at = ServerFixture.TimedBoss;
            await using var killer = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, level: 120, strength: 5000)).Player;
            await using var bystander = (await this.PlayingAtAsync(1002, at.X, at.Y + 1, level: 120)).Player;

            var boss = await killer.Game.WaitForMonsterAsync(
                m => m.Name == "FlyingRooster" && m.X == at.X && m.Y == at.Y,
                TimeSpan.FromSeconds(ServerFixture.BossIntervalSeconds + 5));
            Assert.NotNull(boss);

            // Kill it and verify it drops 2 DragonBalls + 1 SuperDragonGem from drops.json
            await killer.Game.AttackAsync(killer.User.Id, boss.Id, at.X, at.Y);
            Assert.NotNull(await killer.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == boss.Id,
                TimeSpan.FromSeconds(10)));

            var drops = new List<GroundItemMessage>();
            for (int i = 0; i < 3; i++)
            {
                var item = await killer.Game.WaitForGroundItemAsync(
                    g => g.Action == GroundItemMessage.Create && drops.All(d => d.Id != g.Id),
                    TimeSpan.FromSeconds(5));
                Assert.NotNull(item);
                drops.Add(item);
            }
            Assert.Equal(2, drops.Count(d => d.TypeId == ServerFixture.DragonBall.Id));
            Assert.Equal(1, drops.Count(d => d.TypeId == ServerFixture.SuperDragonGem.Id));

            // Bystander cannot pick up killer's boss drop while ownership holds; killer can
            var firstDrop = drops.First(d => d.X == at.X && d.Y == at.Y);
            await bystander.Game.PickUpAsync(firstDrop.Id, firstDrop.X, firstDrop.Y);
            var refused = await this.Servers.WaitForEventAsync("game", "item.ground_rejected",
                e => e.GetProperty("name").GetString() == bystander.User.Name && e.GetProperty("reason").GetString() == "not_owner");
            Assert.Equal(firstDrop.Id, refused.GetProperty("id").GetUInt32());

            await killer.Game.PickUpAsync(firstDrop.Id, firstDrop.X, firstDrop.Y);
            var picked = await killer.Game.WaitForItemAsync(i => i.TypeId == firstDrop.TypeId);
            Assert.NotNull(picked);

            // Wait for the scheduled interval respawn and world Center broadcast
            var notice = await bystander.Game.WaitForTalkAsync(
                t => t.Channel == TalkChannels.Center && t.Text.Contains("FlyingRooster has appeared"),
                TimeSpan.FromSeconds(ServerFixture.BossIntervalSeconds + 6));
            Assert.NotNull(notice);

            var back = await killer.Game.WaitForMonsterAsync(
                m => m.Id == boss.Id,
                TimeSpan.FromSeconds(ServerFixture.BossIntervalSeconds + 6));
            Assert.NotNull(back);
        }

        [Fact]
        public async Task GmCanListAndSpawnScheduledBossWithBroadcastAndOneAliveLimit()
        {
            var at = ServerFixture.ManualBoss;
            await using var gm = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, level: 120, strength: 10000)).Player;
            await using var watcher = (await this.PlayingAtAsync(1002, 430, 380)).Player;

            await CommandAsync(gm, "/boss list");
            var list = await gm.Game.WaitForTalkAsync(t => t.Text.StartsWith("Bosses:") && t.Text.Contains("manual-cooer"));
            Assert.NotNull(list);

            await CommandAsync(gm, "/boss spawn manual-cooer");
            Assert.NotNull(await gm.Game.WaitForTalkAsync(t => t.Text.Contains("Spawned boss manual-cooer")));
            Assert.NotNull(await watcher.Game.WaitForTalkAsync(
                t => t.Channel == TalkChannels.Center && t.Text.Contains("BlueCooer has appeared")));
            var boss = await gm.Game.WaitForMonsterAsync(m => m.Name == "BlueCooer" && m.X == at.X && m.Y == at.Y);
            Assert.NotNull(boss);

            // Trying to spawn a second copy while alive is rejected
            await CommandAsync(gm, "/boss spawn manual-cooer");
            Assert.NotNull(await gm.Game.WaitForTalkAsync(t => t.Text.Contains("already alive")));

            // Killing it uses its drop table (MeteorScroll + ExpBall)
            await gm.Game.AttackAsync(gm.User.Id, boss.Id, at.X, at.Y);
            Assert.NotNull(await gm.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == boss.Id,
                TimeSpan.FromSeconds(10)));

            var drops = new List<GroundItemMessage>();
            for (int i = 0; i < 2; i++)
            {
                var item = await gm.Game.WaitForGroundItemAsync(
                    g => g.Action == GroundItemMessage.Create && drops.All(d => d.Id != g.Id),
                    TimeSpan.FromSeconds(5));
                Assert.NotNull(item);
                drops.Add(item);
            }
            Assert.Contains(drops, d => d.TypeId == ServerFixture.MeteorScroll.Id);
            Assert.Contains(drops, d => d.TypeId == ServerFixture.ExpBall.Id);
        }
    }
}
