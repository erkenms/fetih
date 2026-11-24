namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    public class LuckyTimeTests : IntegrationTest
    {
        private const uint LuckyTime = 28;
        private const ulong Praying = 1UL << 30, Absorbing = 1UL << 31;

        public LuckyTimeTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task APrayerGainsLuckyTimeAndANeighbourAbsorbsItUntilThePrayerMoves()
        {
            var (prayer, prayerEmail) = await this.PlayingAtAsync(1002, 770, 700, GameMaster, level: 100);
            await using var neighbour = (await this.PlayingAtAsync(1002, 772, 700, GameMaster, level: 100)).Player;
            try
            {
                await CommandAsync(prayer, "/learn 9876");
                Assert.NotNull(await prayer.Game.WaitForMagicInfoAsync(m => m.Type == 9876));
                await prayer.Game.CastAsync(prayer.User.Id, 9876, prayer.User.Id, 0, 0);
                Assert.NotNull(await prayer.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Status, out var s) && (s & Praying) != 0));

                // The neighbour two cells away absorbs luck, and the prayer's time grows
                Assert.NotNull(await neighbour.Game.WaitForAttribAsync(a =>
                    a.Values.TryGetValue(Status, out var s) && (s & Absorbing) != 0));
                Assert.NotNull(await prayer.Game.WaitForAttribAsync(a => a.Values.ContainsKey(LuckyTime)));
                await Task.Delay(TimeSpan.FromSeconds(2));

                // Moving ends the prayer, and with it the neighbour's absorbing
                await prayer.Game.WalkAsync(prayer.User.Id, 0);
                Assert.NotNull(await prayer.Game.WaitForAttribAsync(a =>
                    a.Values.TryGetValue(LuckyTime, out var ms) && ms >= 3_000));
                Assert.NotNull(await neighbour.Game.WaitForAttribAsync(a =>
                    a.Values.TryGetValue(Status, out var s) && (s & Absorbing) == 0));
            }
            finally
            {
                await this.LogOutAsync(prayer);
            }

            // The time left is saved with the character
            var saved = Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT LuckyTime FROM `character` WHERE Name = @name", ("@name", prayer.User.Name)));
            Assert.InRange(saved, 1_000u, 7_200_000u);
            await using var relogged = await this.Servers.PlayAsync(prayerEmail, Password);
            Assert.NotNull(await relogged.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(LuckyTime, out var ms) && ms > 0 && ms <= saved));
        }
    }
}
