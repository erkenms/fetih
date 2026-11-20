namespace Comet.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class NobilityTests : IntegrationTest
    {
        private const ushort GameMaster = 3;
        private const uint Silver = 4;
        private const uint King = 12;

        public NobilityTests(ServerFixture servers) : base(servers) { }

        // The only donor of the run tops the board
        [Fact]
        public async Task PlayersDonateForNobilityAndSeeTheBoard()
        {
            var (donor, email) = await this.PlayingAtAsync(1002, 420, 380, GameMaster, level: 70);
            var name = donor.User.Name;
            await using var watcher = (await this.PlayingAtAsync(1002, 421, 380)).Player;
            await using (donor)
            {
                await donor.Game.SayAsync(TalkChannels.Talk, name, "ALLUSERS", "/silver 40000000");
                Assert.NotNull(await donor.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 40_001_000));
                Assert.NotNull(await watcher.Game.WaitForSpawnAsync(donor.User.Id));

                await donor.Game.PeerageAsync(PeerageMessage.Donate, 35_000_000);
                var donated = await donor.Game.WaitForPeerageAsync(p => p.Action == PeerageMessage.Donate);
                Assert.Equal($"{donor.User.Id} 0 35000000 35000000 0", PeerageMessage.Parse(donated).Strings[0]);
                var info = await donor.Game.WaitForPeerageAsync(p => p.Action == PeerageMessage.Info && p.Standing.Total > 0);
                Assert.Equal((donor.User.Id, 35_000_000ul, King, 0), PeerageMessage.Parse(info).Standing);
                Assert.NotNull(await donor.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 5_001_000));
                Assert.NotNull(await watcher.Game.WaitForSpawnAsync(donor.User.Id, s => s.Nobility == King));
                await this.Servers.WaitForEventAsync("game", "nobility.donated", "name", name);

                // Too little, and too low a level
                await donor.Game.PeerageAsync(PeerageMessage.Donate, 1_000_000);
                await this.Servers.WaitForEventAsync("game", "nobility.rejected",
                    e => e.GetProperty("name").GetString() == name && e.GetProperty("reason").GetString() == "amount");
                await watcher.Game.PeerageAsync(PeerageMessage.Donate, 3_000_000);
                await this.Servers.WaitForEventAsync("game", "nobility.rejected",
                    e => e.GetProperty("name").GetString() == watcher.User.Name && e.GetProperty("reason").GetString() == "level");

                await watcher.Game.PeerageAsync(PeerageMessage.Board, 0);
                var board = PeerageMessage.ParseBoard(await watcher.Game.WaitForPeerageAsync(p => p.Action == PeerageMessage.Board));
                Assert.Equal(1, board.Pages);
                Assert.Equal((donor.User.Id, name, 35_000_000ul, King, 0), board.Entries.Single());

                // A knight needs 30M; with fewer than three donors, one donation makes a king
                await watcher.Game.PeerageAsync(PeerageMessage.Minimum, 1);
                var knight = PeerageMessage.ParseMinimum(await watcher.Game.WaitForPeerageAsync(p => p.Action == PeerageMessage.Minimum));
                Assert.Equal((30_000_000ul, 1, -1), knight);
                await watcher.Game.PeerageAsync(PeerageMessage.Minimum, King);
                var king = PeerageMessage.ParseMinimum(await watcher.Game.WaitForPeerageAsync(p => p.Action == PeerageMessage.Minimum));
                Assert.Equal((3_000_000ul, 1, -1), king);
            }

            // Saved, and shown again on login
            await this.Servers.WaitForLogoutSaveAsync(name);
            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.Equal(King, again.User.Nobility);
        }
    }
}
