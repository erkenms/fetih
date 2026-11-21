namespace Comet.Integration.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class NobilityTests : IntegrationTest
    {
        private const uint Silver = 4;
        private const uint Knight = 1, Duke = 7, Prince = 9, King = 12;
        private const ulong Donation = 35_000_000, KnightDonation = 30_000_000, MinDonation = 3_000_000;

        public NobilityTests(ServerFixture servers) : base(servers) { }

        // Every donor on the board, best first: other tests' too
        private static async Task<List<(uint Id, string Name, ulong Donation, uint Rank, int Position)>> BoardAsync(Player player)
        {
            var entries = new List<(uint, string, ulong, uint, int)>();
            for (int page = 0, pages = 1; page < pages; page++)
            {
                await player.Game.PeerageAsync(PeerageMessage.Board, (ulong)page);
                var board = PeerageMessage.ParseBoard(await player.Game.WaitForPeerageAsync(p => p.Action == PeerageMessage.Board));
                pages = board.Pages;
                entries.AddRange(board.Entries);
            }
            return entries;
        }

        // The top 3 are kings, the next 12 princes, the rest of the top 50 dukes
        private static uint RankAt(int position) => position < 3 ? King : position < 15 ? Prince : position < 50 ? Duke : Knight;

        [Fact]
        public async Task PlayersDonateForNobilityAndSeeTheBoard()
        {
            var (donor, email) = await this.PlayingAtAsync(1002, 420, 380, GameMaster, level: 70);
            var name = donor.User.Name;
            await using var watcher = (await this.PlayingAtAsync(1002, 421, 380)).Player;
            uint rank;
            await using (donor)
            {
                await CommandAsync(donor, "/silver 40000000");
                Assert.NotNull(await donor.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 40_001_000));
                Assert.NotNull(await watcher.Game.WaitForSpawnAsync(donor.User.Id));

                // Earlier donors keep their places; ties go to the earlier character
                var before = await BoardAsync(watcher);
                int position = before.Count(e => e.Donation >= Donation);
                rank = RankAt(position);

                await donor.Game.PeerageAsync(PeerageMessage.Donate, Donation);
                var donated = await donor.Game.WaitForPeerageAsync(p => p.Action == PeerageMessage.Donate);
                Assert.Equal($"{donor.User.Id} 0 {Donation} {Donation} {position}", PeerageMessage.Parse(donated).Strings[0]);
                var info = await donor.Game.WaitForPeerageAsync(p => p.Action == PeerageMessage.Info && p.Standing.Total > 0);
                Assert.Equal((donor.User.Id, Donation, rank, position), PeerageMessage.Parse(info).Standing);
                Assert.NotNull(await donor.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 5_001_000));
                Assert.NotNull(await watcher.Game.WaitForSpawnAsync(donor.User.Id, s => s.Nobility == rank));
                await this.Servers.WaitForEventAsync("game", "nobility.donated", "name", name);

                // Too little, and too low a level
                await donor.Game.PeerageAsync(PeerageMessage.Donate, 1_000_000);
                await this.Servers.WaitForEventAsync("game", "nobility.rejected",
                    e => e.GetProperty("name").GetString() == name && e.GetProperty("reason").GetString() == "amount");
                await watcher.Game.PeerageAsync(PeerageMessage.Donate, 3_000_000);
                await this.Servers.WaitForEventAsync("game", "nobility.rejected",
                    e => e.GetProperty("name").GetString() == watcher.User.Name && e.GetProperty("reason").GetString() == "level");

                var after = await BoardAsync(watcher);
                Assert.Equal((donor.User.Id, name, Donation, rank, position), Assert.Single(after, e => e.Id == donor.User.Id));

                // What the watcher, who gave nothing, would need: a knight's fixed 30M, and one
                // more than the third donor for a king (one donation while there are fewer)
                var donations = after.Select(e => e.Donation).ToList();
                await watcher.Game.PeerageAsync(PeerageMessage.Minimum, Knight);
                var knight = PeerageMessage.ParseMinimum(await watcher.Game.WaitForPeerageAsync(p => p.Action == PeerageMessage.Minimum));
                Assert.Equal((KnightDonation, donations.Count(d => d >= KnightDonation), -1), knight);
                await watcher.Game.PeerageAsync(PeerageMessage.Minimum, King);
                var king = PeerageMessage.ParseMinimum(await watcher.Game.WaitForPeerageAsync(p => p.Action == PeerageMessage.Minimum));
                ulong kingTotal = donations.Count < 3 ? MinDonation : donations[2] + 1;
                Assert.Equal((kingTotal, donations.Count(d => d >= kingTotal), -1), king);
                await this.LogOutAsync(donor);
            }

            // Saved, and shown again on login
            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.Equal(rank, again.User.Nobility);
        }
    }
}
