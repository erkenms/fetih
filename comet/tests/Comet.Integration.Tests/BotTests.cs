namespace Comet.Integration.Tests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Bots;
    using Comet.Client;
    using Comet.Game.World;
    using Xunit;

    public class BotTests : IntegrationTest
    {
        public BotTests(ServerFixture servers) : base(servers) { }

        private BotsConfiguration Config(string prefix)
        {
            var (host, user, password) = this.Servers.Database;
            return new BotsConfiguration
            {
                AccountServer = new BotsConfiguration.ServerAddress
                {
                    Host = ServerFixture.Host, Port = this.Servers.AccountPort, Realm = ServerFixture.Realm,
                },
                GameDatabase = new BotsConfiguration.Database
                {
                    Hostname = host, Schema = this.Servers.GameSchema, Username = user, Password = password,
                },
                Password = Password,
                Prefix = prefix,
            };
        }

        [Fact]
        public async Task HuntingBotsComeToTheirSpotAndKillMonstersInSightOfOthers()
        {
            var at = ServerFixture.PheasantForBots;
            await using var watcher = (await this.PlayingAtAsync(1002, at.X + 6, at.Y)).Player;
            var pheasant = await watcher.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);

            var group = new BotsConfiguration.Group
            {
                Behavior = "hunt", Count = 1, Map = 1002, X = (ushort)(at.X - 4), Y = at.Y, Spread = 2, Level = 10,
            };
            var grid = new MapStore(this.Servers.MapDirectory).Get(1002);
            var bot = new Bot(1, this.Config("Hunter"), group, grid, new BotStats());
            using var stop = new CancellationTokenSource();
            var running = bot.RunAsync(stop.Token);
            try
            {
                // Its first login makes the character, the second puts it on the spot
                var spawn = await watcher.Game.WaitForPlayerAsync(s => s.Name == bot.Name, TimeSpan.FromSeconds(30));
                Assert.NotNull(spawn);
                Assert.True(Math.Abs(spawn.X - group.X) <= group.Spread && Math.Abs(spawn.Y - group.Y) <= group.Spread);

                var kill = await watcher.Game.WaitForInteractAsync(
                    i => i.Action == InteractMessage.Kill && i.Target == pheasant.Id, TimeSpan.FromSeconds(20));
                Assert.Equal(spawn.Id, kill?.Sender);
            }
            finally
            {
                stop.Cancel();
                await running;
            }
        }
    }
}
