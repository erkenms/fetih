namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class BroadcastTests : IntegrationTest
    {
        private const uint Jewels = 29;

        public BroadcastTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task BroadcastNeedsLevel50AndConquerPoints()
        {
            await using var young = (await this.PlayingAtAsync(1002, 430, 380, GameMaster, level: 49)).Player;
            await CommandAsync(young, "/jewel 50");
            Assert.NotNull(await young.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Jewels, out var v) && v == 50));

            await young.Game.PigeonAsync(PigeonMessage.Send, 0, "too young");
            var levelNotice = await young.Game.WaitForTalkAsync(
                t => t.Channel == TalkChannels.Service && t.Text.Contains("level 50"));
            Assert.NotNull(levelNotice);

            await using var broke = (await this.PlayingAtAsync(1002, 432, 380, level: 50)).Player;
            await broke.Game.PigeonAsync(PigeonMessage.Send, 0, "no cps");
            var cpNotice = await broke.Game.WaitForTalkAsync(
                t => t.Channel == TalkChannels.Service && t.Text.Contains("enough CPs"));
            Assert.NotNull(cpNotice);
        }

        [Fact]
        public async Task BroadcastQueuesListsPromotesAndReleasesToAllPlayers()
        {
            await using var sender = (await this.PlayingAtAsync(1002, 435, 380, GameMaster, level: 50)).Player;
            await using var listener = (await this.PlayingAtAsync(1006, 20, 20, level: 1)).Player;

            await CommandAsync(sender, "/jewel 200");
            Assert.NotNull(await sender.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Jewels, out var v) && v == 200));

            // Queue 7 messages back-to-back: 1st may release soon, leaving the rest in the queue
            // so Urgent 5CPs and Urgent 15CPs can be exercised against real queue positions
            for (int i = 1; i <= 7; i++)
            {
                await sender.Game.PigeonAsync(PigeonMessage.Send, 0, $"bc-{i}");
                var ack = await sender.Game.WaitForPigeonAsync(p => p.Action == PigeonMessage.Send);
                Assert.NotNull(ack);
                Assert.True(ack.Param >= 1);
            }

            // Check window: Release soon (QueryAll) and Info awaiting (QueryMine)
            await sender.Game.PigeonAsync(PigeonMessage.QueryAll, 0);
            var list = await sender.Game.WaitForPigeonQueryAsync();
            Assert.NotNull(list);
            var target = list.Entries.Last(e => e.CharacterId == sender.User.Id && e.Message == "bc-7");
            Assert.True(target.Position >= 1);

            await sender.Game.PigeonAsync(PigeonMessage.QueryMine, 0);
            var mine = await sender.Game.WaitForPigeonQueryAsync();
            Assert.NotNull(mine);
            Assert.All(mine.Entries, e => Assert.Equal(sender.User.Id, e.CharacterId));

            // Urgent 5CPs moves bc-7 forward up to 5 places; Urgent 15CPs moves it to 1st
            if (target.Position > 1)
            {
                await sender.Game.PigeonAsync(PigeonMessage.Urgent, target.Id);
                var urgentReply = await sender.Game.WaitForPigeonAsync(p => p.Action == PigeonMessage.Urgent);
                Assert.NotNull(urgentReply);
                Assert.True(urgentReply.Param <= target.Position);
            }

            // Query again; if bc-7 isn't already 1st, promote it to 1st for 15 CPs
            await sender.Game.PigeonAsync(PigeonMessage.QueryMine, 0);
            var afterFive = await sender.Game.WaitForPigeonQueryAsync();
            var updated = afterFive.Entries.FirstOrDefault(e => e.Id == target.Id);
            if (updated != null && updated.Position > 0)
            {
                await sender.Game.PigeonAsync(PigeonMessage.UrgentTop, target.Id);
                var topReply = await sender.Game.WaitForPigeonAsync(p => p.Action == PigeonMessage.UrgentTop);
                Assert.NotNull(topReply);
                Assert.Equal(1u, topReply.Param);
            }

            // Listener on another map receives the broadcast on TalkChannel.Broadcast
            var received = await listener.Game.WaitForTalkAsync(
                t => t.Channel == TalkChannels.Broadcast && t.Sender == sender.User.Name && t.Text == "bc-7",
                TimeSpan.FromSeconds(10));
            Assert.NotNull(received);
        }
    }
}
