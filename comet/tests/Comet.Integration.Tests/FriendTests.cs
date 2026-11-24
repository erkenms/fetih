namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class FriendTests : IntegrationTest
    {
        public FriendTests(ServerFixture servers) : base(servers) { }

        private async Task<int> RowsAsync(string table, string column, uint characterId, uint otherId) =>
            Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                $"SELECT COUNT(*) FROM `{table}` WHERE CharacterID = @a AND {column} = @b OR " +
                $"CharacterID = @b AND {column} = @a", ("@a", characterId), ("@b", otherId)));

        [Fact]
        public async Task FriendsAreKeptAcrossLoginsUntilOneBreaksUp()
        {
            await using var asker = (await this.PlayingAtAsync(1002, 200, 280)).Player;
            var (friend, email) = await this.PlayingAtAsync(1002, 201, 280);

            await asker.Game.FriendAsync(FriendMessage.Apply, friend.User.Id, friend.User.Name);
            Assert.NotNull(await friend.Game.WaitForRelationAsync(r => r.AskerId == asker.User.Id));
            var asked = await friend.Game.WaitForFriendAsync(f => f.Action == FriendMessage.Apply);
            Assert.Equal((asker.User.Id, asker.User.Name), (asked?.Id, asked?.Name));
            await friend.Game.FriendAsync(FriendMessage.Accept, asker.User.Id);
            Assert.NotNull(await asker.Game.WaitForFriendAsync(f => f.Action == FriendMessage.GetInfo && f.Id == friend.User.Id && f.IsOnline));
            Assert.NotNull(await friend.Game.WaitForFriendAsync(f => f.Action == FriendMessage.GetInfo && f.Id == asker.User.Id));
            Assert.Equal(2, await this.RowsAsync("friend", "FriendID", asker.User.Id, friend.User.Id));
            await this.Servers.WaitForEventAsync("game", "friend.added", "name", asker.User.Name);

            await friend.Game.SayAsync(TalkChannels.Friend, friend.User.Name, "ALLUSERS", "hi friends");
            Assert.NotNull(await asker.Game.WaitForTalkAsync(t => t.Text == "hi friends"));

            await friend.DisposeAsync();
            Assert.NotNull(await asker.Game.WaitForFriendAsync(f => f.Action == FriendMessage.Offline && f.Id == friend.User.Id));
            await using (var back = await this.Servers.PlayAsync(email, Password))
            {
                var listed = back.Game.Relationships.SingleOrDefault(f => f.Id == asker.User.Id);
                Assert.Equal((FriendMessage.GetInfo, true, asker.User.Name), (listed?.Action, listed?.IsOnline, listed?.Name));
                Assert.NotNull(await asker.Game.WaitForFriendAsync(f => f.Action == FriendMessage.Online && f.Id == friend.User.Id));

                await asker.Game.FriendAsync(FriendMessage.Break, friend.User.Id);
                Assert.NotNull(await asker.Game.WaitForFriendAsync(f => f.Action == FriendMessage.Break && f.Id == friend.User.Id));
                Assert.NotNull(await back.Game.WaitForFriendAsync(f => f.Action == FriendMessage.Break && f.Id == asker.User.Id));
            }
            Assert.Equal(0, await this.RowsAsync("friend", "FriendID", asker.User.Id, friend.User.Id));
        }

        [Fact]
        public async Task AskingEachOtherMakesFriends()
        {
            await using var first = (await this.PlayingAtAsync(1002, 210, 280)).Player;
            await using var second = (await this.PlayingAtAsync(1002, 211, 280)).Player;
            await first.Game.FriendAsync(FriendMessage.Apply, second.User.Id);
            Assert.NotNull(await second.Game.WaitForFriendAsync(f => f.Action == FriendMessage.Apply));
            await second.Game.FriendAsync(FriendMessage.Apply, first.User.Id);
            Assert.NotNull(await first.Game.WaitForFriendAsync(f => f.Action == FriendMessage.GetInfo && f.Id == second.User.Id));

            await first.Game.FriendAsync(FriendMessage.Apply, second.User.Id);
            Assert.NotNull(await first.Game.WaitForTalkAsync(t => t.Text == $"{second.User.Name} is already your friend."));
        }

        [Fact]
        public async Task EnemiesAreListedAndCanBeDeleted()
        {
            await using var killer = (await this.PlayingAtAsync(1002, 220, 280)).Player;
            var (victim, email) = await this.PlayingAtAsync(1002, 221, 280);
            await victim.DisposeAsync();
            await this.Servers.ExecuteGameSqlAsync("INSERT INTO enemy (CharacterID, EnemyID) VALUES (@a, @b)",
                ("@a", victim.User.Id), ("@b", killer.User.Id));

            await using var back = await this.Servers.PlayAsync(email, Password);
            var listed = back.Game.Relationships.SingleOrDefault(f => f.Id == killer.User.Id);
            Assert.Equal((FriendMessage.EnemyAdd, true, killer.User.Name), (listed?.Action, listed?.IsOnline, listed?.Name));

            await back.Game.FriendAsync(FriendMessage.EnemyDelete, killer.User.Id);
            Assert.NotNull(await back.Game.WaitForFriendAsync(f => f.Action == FriendMessage.EnemyDelete && f.Id == killer.User.Id));
            await this.Servers.WaitForEventAsync("game", "enemy.removed", "name", victim.User.Name);
            Assert.Equal(0, await this.RowsAsync("enemy", "EnemyID", victim.User.Id, killer.User.Id));
        }

        [Fact]
        public async Task EnemyListReturnsOnlineAndOfflineEnemiesAndReflectsDeletion()
        {
            await using var onlineKiller = (await this.PlayingAtAsync(1002, 224, 280)).Player;
            var (offlineKiller, _) = await this.PlayingAtAsync(1002, 225, 280);
            await offlineKiller.DisposeAsync();
            var (victim, email) = await this.PlayingAtAsync(1002, 226, 280);
            await victim.DisposeAsync();

            await this.Servers.ExecuteGameSqlAsync(
                "INSERT INTO enemy (CharacterID, EnemyID) VALUES (@v, @a), (@v, @b)",
                ("@v", victim.User.Id), ("@a", onlineKiller.User.Id), ("@b", offlineKiller.User.Id));

            await using var back = await this.Servers.PlayAsync(email, Password);
            await back.Game.RequestEnemyListAsync(back.User.Id);
            var list = await back.Game.WaitForEnemyListAsync();
            Assert.NotNull(list);
            Assert.Equal(back.User.Id, list.UserId);
            Assert.Equal(2, list.Entries.Count);

            var onlineEntry = Assert.Single(list.Entries, e => e.Id == onlineKiller.User.Id);
            Assert.Equal((onlineKiller.User.Name, true, 1), (onlineEntry.Name, onlineEntry.IsOnline, onlineEntry.Gender));
            var offlineEntry = Assert.Single(list.Entries, e => e.Id == offlineKiller.User.Id);
            Assert.Equal((offlineKiller.User.Name, false, 1), (offlineEntry.Name, offlineEntry.IsOnline, offlineEntry.Gender));

            await back.Game.FriendAsync(FriendMessage.EnemyDelete, onlineKiller.User.Id);
            Assert.NotNull(await back.Game.WaitForFriendAsync(f => f.Action == FriendMessage.EnemyDelete && f.Id == onlineKiller.User.Id));

            await back.Game.RequestEnemyListAsync(back.User.Id);
            var afterDelete = await back.Game.WaitForEnemyListAsync();
            Assert.NotNull(afterDelete);
            var remaining = Assert.Single(afterDelete.Entries);
            Assert.Equal(offlineKiller.User.Id, remaining.Id);
        }

        [Fact]
        public async Task PkKillAddsKillerAsEnemyOnceAndSurvivesRelog()
        {
            await using var attacker = (await this.PlayingAtAsync(ServerFixture.DesertCity, 140, 100, strength: 5000)).Player;
            var (victim, email) = await this.PlayingAtAsync(ServerFixture.DesertCity, 141, 100);
            await FreePkAsync(attacker);

            await attacker.Game.AttackAsync(attacker.User.Id, victim.User.Id, 141, 100);
            var kill = await victim.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == victim.User.Id, TimeSpan.FromSeconds(15));
            Assert.Equal(attacker.User.Id, kill?.Sender);

            var added = await victim.Game.WaitForFriendAsync(
                f => f.Action == FriendMessage.EnemyAdd && f.Id == attacker.User.Id);
            Assert.Equal((attacker.User.Name, true), (added?.Name, added?.IsOnline));
            await this.Servers.WaitForEventAsync("game", "enemy.added", "name", victim.User.Name);
            Assert.Equal(1, await this.RowsAsync("enemy", "EnemyID", victim.User.Id, attacker.User.Id));

            await victim.Game.RequestEnemyListAsync(victim.User.Id);
            var list = await victim.Game.WaitForEnemyListAsync();
            var entry = Assert.Single(list.Entries);
            Assert.Equal((attacker.User.Id, attacker.User.Name, true), (entry.Id, entry.Name, entry.IsOnline));

            // Relog: the enemy row survives, and a second kill doesn't duplicate it
            await this.LogOutAsync(victim);
            await using var back = await this.Servers.PlayAsync(email, Password);
            var listed = back.Game.Relationships.SingleOrDefault(f => f.Id == attacker.User.Id);
            Assert.Equal((FriendMessage.EnemyAdd, true, attacker.User.Name), (listed?.Action, listed?.IsOnline, listed?.Name));

            await attacker.Game.AttackAsync(attacker.User.Id, back.User.Id, 141, 100);
            var secondKill = await back.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == back.User.Id, TimeSpan.FromSeconds(15));
            Assert.Equal(attacker.User.Id, secondKill?.Sender);
            await SyncAsync(back);
            Assert.Null(await back.Game.WaitForFriendAsync(
                f => f.Action == FriendMessage.EnemyAdd && f.Id == attacker.User.Id, Already));
            Assert.Equal(1, await this.RowsAsync("enemy", "EnemyID", victim.User.Id, attacker.User.Id));
        }

        [Fact]
        public async Task EnemyListRespectsMaxLimit()
        {
            await using var killer = (await this.PlayingAtAsync(ServerFixture.DesertCity, 150, 100, strength: 5000)).Player;
            await FreePkAsync(killer);
            var (victim, email) = await this.PlayingAtAsync(ServerFixture.DesertCity, 151, 100);
            await this.LogOutAsync(victim);

            for (int i = 0; i < 5; i++)
            {
                var (other, _) = await this.PlayingAsync();
                await this.LogOutAsync(other);
                await this.Servers.ExecuteGameSqlAsync(
                    "INSERT INTO enemy (CharacterID, EnemyID) VALUES (@v, @e)",
                    ("@v", victim.User.Id), ("@e", other.User.Id));
            }

            await using var back = await this.Servers.PlayAsync(email, Password);
            Assert.Equal(5, back.Game.Relationships.Count(f => f.Action == FriendMessage.EnemyAdd));

            await killer.Game.AttackAsync(killer.User.Id, back.User.Id, 151, 100);
            var kill = await back.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == back.User.Id, TimeSpan.FromSeconds(15));
            Assert.Equal(killer.User.Id, kill?.Sender);
            await SyncAsync(back);
            Assert.Null(await back.Game.WaitForFriendAsync(
                f => f.Action == FriendMessage.EnemyAdd && f.Id == killer.User.Id, Already));
            Assert.Equal(0, await this.RowsAsync("enemy", "EnemyID", victim.User.Id, killer.User.Id));
        }
    }
}
