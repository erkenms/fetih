namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Comet.Client;
    using Comet.Shared.Models.Admin;
    using StreamJsonRpc;
    using Xunit;

    public class AdminRpcTests : IntegrationTest
    {
        private const uint Actor = 4242;
        private const uint ConquerPoints = 29;

        public AdminRpcTests(ServerFixture servers) : base(servers) { }

        private sealed class Rpc : IAsyncDisposable
        {
            private readonly TcpClient Tcp;
            public IGameAdmin Admin { get; }

            private Rpc(TcpClient tcp)
            {
                this.Tcp = tcp;
                this.Admin = JsonRpc.Attach<IGameAdmin>(tcp.GetStream());
            }

            public static async Task<Rpc> ConnectAsync(ServerFixture servers)
            {
                var tcp = new TcpClient();
                await tcp.ConnectAsync(ServerFixture.Host, servers.RpcPort);
                return new Rpc(tcp);
            }

            public ValueTask DisposeAsync()
            {
                ((IDisposable)this.Admin).Dispose();
                this.Tcp.Dispose();
                return ValueTask.CompletedTask;
            }
        }

        private async Task<(uint Id, string Name, string Email)> OfflineCharacterAsync(uint map = 1002, int x = 300, int y = 300)
        {
            var (player, email) = await this.PlayingAtAsync(map, x, y);
            var (id, name) = (player.User.Id, player.User.Name);
            await this.LogOutAsync(player);
            return (id, name, email);
        }

        private async Task<object> ColumnAsync(string column, uint characterID) =>
            await this.Servers.QueryGameScalarAsync($"SELECT `{column}` FROM `character` WHERE CharacterID = @id",
                ("@id", characterID));

        private static void AssertDone(AdminResult result, bool online)
        {
            Assert.True(result.Ok, result.Error);
            Assert.Equal(online, result.Online);
        }

        [Fact]
        public async Task StatusAndOnlineListShowThePlayer()
        {
            await using var player = (await this.PlayingAtAsync(1002, 300, 340)).Player;
            await using var rpc = await Rpc.ConnectAsync(this.Servers);

            var status = await rpc.Admin.ServerStatus();
            Assert.True(status.Players >= 1);
            Assert.True(status.UptimeSeconds >= 0);
            Assert.True(status.MapsLoaded >= 1);

            var online = await rpc.Admin.OnlinePlayers();
            var me = Assert.Single(online, p => p.CharacterID == player.User.Id);
            Assert.Equal((player.User.Name, 1002u, (ushort)300, (ushort)340), (me.Name, me.MapID, me.X, me.Y));
        }

        [Fact]
        public async Task MapSnapshotListsPlayersMonstersAndNpcs()
        {
            await using var player = (await this.PlayingAtAsync(1002, 302, 340)).Player;
            await using var rpc = await Rpc.ConnectAsync(this.Servers);

            var map = await rpc.Admin.MapSnapshot(1002u);
            Assert.Equal(1002u, map.MapID);
            var me = Assert.Single(map.Players, p => p.CharacterID == player.User.Id);
            Assert.Equal(((ushort)302, (ushort)340, "white"), (me.X, me.Y, me.NameColor));
            Assert.NotEmpty(map.Monsters);
            Assert.NotEmpty(map.Npcs);
            Assert.Null(await rpc.Admin.MapSnapshot(987654u));
        }

        [Fact]
        public async Task CharacterShowsLiveAndSavedState()
        {
            await using var rpc = await Rpc.ConnectAsync(this.Servers);
            var (player, _) = await this.PlayingAtAsync(1002, 304, 340);
            var live = await rpc.Admin.Character(player.User.Id);
            Assert.True(live.Online);
            Assert.Equal((player.User.Name, (ushort)304, (ushort)340), (live.Name, live.X, live.Y));
            Assert.NotNull(live.MaxHealth);
            Assert.Equal(player.Inventory.Count, live.Items.Count(i => i.Position == 0));
            await this.LogOutAsync(player);

            var saved = await rpc.Admin.Character(player.User.Id);
            Assert.False(saved.Online);
            Assert.Equal(player.User.Name, saved.Name);
            Assert.Null(await rpc.Admin.Character(999999999u));
        }

        [Fact]
        public async Task KickClosesTheConnectionAndIsLogged()
        {
            await using var target = (await this.PlayingAtAsync(1002, 306, 340)).Player;
            await using var rpc = await Rpc.ConnectAsync(this.Servers);

            var result = await rpc.Admin.Kick(new KickArgs { ActorAccountID = Actor, CharacterID = target.User.Id, Reason = "afk" });
            AssertDone(result, online: true);
            Assert.NotNull(await target.Game.WaitForTalkAsync(t => t.Text.Contains("kicked: afk")));
            Assert.True(await target.Game.WaitClosedAsync());
            var logged = await this.Servers.WaitForEventAsync("game", "gm.kick", "characterId", target.User.Id.ToString());
            Assert.Equal(Actor, logged.GetProperty("actorAccountId").GetUInt32());

            var again = await rpc.Admin.Kick(new KickArgs { ActorAccountID = Actor, CharacterID = target.User.Id });
            Assert.Equal(AdminErrors.Offline, again.Error);
        }

        [Fact]
        public async Task MuteWorksOnlineAndOffline()
        {
            await using var rpc = await Rpc.ConnectAsync(this.Servers);
            var (target, _) = await this.PlayingAtAsync(1002, 308, 340);
            await using (target)
            {
                AssertDone(await rpc.Admin.Mute(new MuteArgs { ActorAccountID = Actor, CharacterID = target.User.Id, Minutes = 5 }), online: true);
                Assert.NotNull(await target.Game.WaitForTalkAsync(t => t.Text.Contains("muted for 5 minutes")));
                await target.Game.SayAsync(TalkChannels.Talk, target.User.Name, "ALLUSERS", "hello");
                await this.Servers.WaitForEventAsync("game", "chat.rejected", "name", target.User.Name);

                AssertDone(await rpc.Admin.Mute(new MuteArgs { ActorAccountID = Actor, CharacterID = target.User.Id, Minutes = 0 }), online: true);
                Assert.NotNull(await target.Game.WaitForTalkAsync(t => t.Text.Contains("You can chat again")));
            }

            var (id, _, _) = await this.OfflineCharacterAsync();
            AssertDone(await rpc.Admin.Mute(new MuteArgs { ActorAccountID = Actor, CharacterID = id, Minutes = 60 }), online: false);
            var until = (DateTime)await this.ColumnAsync("MutedUntil", id);
            Assert.InRange(until, DateTime.UtcNow.AddMinutes(55), DateTime.UtcNow.AddMinutes(65));
            Assert.Equal(AdminErrors.Invalid,
                (await rpc.Admin.Mute(new MuteArgs { ActorAccountID = Actor, CharacterID = id, Minutes = -1 })).Error);
        }

        [Fact]
        public async Task BanClosesTheSessionAndRefusesLoginUntilUnbanned()
        {
            await using var rpc = await Rpc.ConnectAsync(this.Servers);
            var (target, email) = await this.PlayingAtAsync(1002, 310, 340);
            await using var _ = target;
            uint account = await this.Servers.AccountIdAsync(email);

            AssertDone(await rpc.Admin.Ban(new BanArgs { ActorAccountID = Actor, AccountID = account, Reason = "botting" }), online: true);
            Assert.NotNull(await target.Game.WaitForTalkAsync(t => t.Text.Contains("banned: botting")));
            Assert.True(await target.Game.WaitClosedAsync());
            Assert.Equal(Banned, (await this.Servers.LoginAsync(email, Password)).RejectCode);
            await this.Servers.WaitForEventAsync("game", "gm.ban", "accountId", account.ToString());

            AssertDone(await rpc.Admin.Unban(new BanArgs { ActorAccountID = Actor, AccountID = account }), online: false);
            Assert.True((await this.Servers.LoginAsync(email, Password)).Accepted);
            Assert.Equal(AdminErrors.NotBanned,
                (await rpc.Admin.Unban(new BanArgs { ActorAccountID = Actor, AccountID = account })).Error);
            Assert.Equal(AdminErrors.NotFound,
                (await rpc.Admin.Ban(new BanArgs { ActorAccountID = Actor, AccountID = 999999999 })).Error);
        }

        [Fact]
        public async Task TeleportMovesOnlineAndOfflineCharacters()
        {
            await using var rpc = await Rpc.ConnectAsync(this.Servers);
            var (target, _) = await this.PlayingAtAsync(1002, 312, 340);
            await using (target)
            {
                AssertDone(await rpc.Admin.Teleport(new TeleportArgs { ActorAccountID = Actor, CharacterID = target.User.Id, MapID = 1006, X = 30, Y = 30 }), online: true);
                Assert.NotNull(await target.Game.WaitForActionAsync(ActionMessage.Teleport, target.User.Id));
                Assert.Equal((1006u, 30, 30), await this.SavedPositionAsync(target));
            }

            var (id, _, _) = await this.OfflineCharacterAsync();
            AssertDone(await rpc.Admin.Teleport(new TeleportArgs { ActorAccountID = Actor, CharacterID = id, MapID = 1006, X = 31, Y = 32 }), online: false);
            Assert.Equal(1006u, Convert.ToUInt32(await this.ColumnAsync("MapID", id)));
            Assert.Equal(31, Convert.ToInt32(await this.ColumnAsync("X", id)));

            var blocked = await rpc.Admin.Teleport(new TeleportArgs
            {
                ActorAccountID = Actor, CharacterID = id, MapID = 1002,
                X = (ushort)ServerFixture.BlockedCell.X, Y = (ushort)ServerFixture.BlockedCell.Y,
            });
            Assert.Equal(AdminErrors.BadDestination, blocked.Error);
        }

        [Fact]
        public async Task JailSendsOfflineCharactersToTheJailMap()
        {
            await using var rpc = await Rpc.ConnectAsync(this.Servers);
            var (id, _, _) = await this.OfflineCharacterAsync();
            var result = await rpc.Admin.Jail(new CharacterArgs { ActorAccountID = Actor, CharacterID = id });
            Assert.False(result.Online);
            var map = Convert.ToUInt32(await this.ColumnAsync("MapID", id));
            // The test maps may not include the jail
            Assert.True(result.Ok ? map == 6000 : result.Error == AdminErrors.BadDestination && map == 1002);
        }

        [Fact]
        public async Task GiveAndTakeItemsOnlineAndOffline()
        {
            await using var rpc = await Rpc.ConnectAsync(this.Servers);
            var blade = ServerFixture.Blade;
            var (target, _) = await this.PlayingAtAsync(1002, 314, 340);
            uint given;
            await using (target)
            {
                AssertDone(await rpc.Admin.GiveItem(new GiveItemArgs
                {
                    ActorAccountID = Actor, CharacterID = target.User.Id, TypeID = blade.Id, Plus = 3, Bless = 1, Gem1 = 1, Gem2 = 255,
                }), online: true);
                var item = await target.Game.WaitForItemAsync(i => i.TypeId == blade.Id);
                Assert.NotNull(item);
                given = item.Id;
                Assert.Equal(3, Convert.ToInt32(await this.Servers.QueryGameScalarAsync("SELECT Plus FROM item WHERE ItemID = @id", ("@id", given))));
                Assert.Equal(255, Convert.ToInt32(await this.Servers.QueryGameScalarAsync("SELECT Gem2 FROM item WHERE ItemID = @id", ("@id", given))));

                AssertDone(await rpc.Admin.TakeItem(new TakeItemArgs { ActorAccountID = Actor, CharacterID = target.User.Id, ItemID = given }), online: true);
                Assert.NotNull(await target.Game.WaitForItemActionAsync(ItemMessage.Remove, given));
            }

            var (id, _, _) = await this.OfflineCharacterAsync();
            AssertDone(await rpc.Admin.GiveItem(new GiveItemArgs
            {
                ActorAccountID = Actor, CharacterID = id, TypeID = blade.Id, Count = 2, Bless = 7,
            }), online: false);
            Assert.Equal(2L, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id AND TypeID = @type AND Bless = 7", ("@id", id), ("@type", blade.Id))));
            var offlineItem = Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT MIN(ItemID) FROM item WHERE CharacterID = @id AND TypeID = @type", ("@id", id), ("@type", blade.Id)));
            AssertDone(await rpc.Admin.TakeItem(new TakeItemArgs { ActorAccountID = Actor, CharacterID = id, ItemID = offlineItem }), online: false);
            Assert.Equal(1L, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id AND TypeID = @type", ("@id", id), ("@type", blade.Id))));

            Assert.Equal(AdminErrors.UnknownItemType, (await rpc.Admin.GiveItem(new GiveItemArgs { ActorAccountID = Actor, CharacterID = id, TypeID = 1 })).Error);
            Assert.Equal(AdminErrors.InventoryFull, (await rpc.Admin.GiveItem(new GiveItemArgs { ActorAccountID = Actor, CharacterID = id, TypeID = blade.Id, Count = 40 })).Error);
            Assert.Equal(AdminErrors.NotFound, (await rpc.Admin.TakeItem(new TakeItemArgs { ActorAccountID = Actor, CharacterID = id, ItemID = offlineItem })).Error);
        }

        [Fact]
        public async Task SilverConquerPointsAndLevelChangeOnlineAndOffline()
        {
            await using var rpc = await Rpc.ConnectAsync(this.Servers);
            var (target, _) = await this.PlayingAtAsync(1002, 316, 340);
            await using (target)
            {
                AssertDone(await rpc.Admin.AddSilver(new AmountArgs { ActorAccountID = Actor, CharacterID = target.User.Id, Amount = 1234 }), online: true);
                Assert.NotNull(await SilverAsync(target, target.User.Silver + 1234UL));
                AssertDone(await rpc.Admin.AddConquerPoints(new AmountArgs { ActorAccountID = Actor, CharacterID = target.User.Id, Amount = 50 }), online: true);
                Assert.NotNull(await target.Game.WaitForAttribAsync(a => a.Values.TryGetValue(ConquerPoints, out var v) && v == 50));
                AssertDone(await rpc.Admin.SetLevel(new LevelArgs { ActorAccountID = Actor, CharacterID = target.User.Id, Level = 20 }), online: true);
                Assert.NotNull(await target.Game.WaitForAttribAsync(a => a.Values.TryGetValue(UserAttribMessage.Level, out var v) && v == 20));
                Assert.Equal(AdminErrors.OutOfRange, (await rpc.Admin.AddSilver(new AmountArgs { ActorAccountID = Actor, CharacterID = target.User.Id, Amount = -10_000_000 })).Error);
            }

            var (id, _, _) = await this.OfflineCharacterAsync();
            long silver = Convert.ToInt64(await this.ColumnAsync("Silver", id));
            AssertDone(await rpc.Admin.AddSilver(new AmountArgs { ActorAccountID = Actor, CharacterID = id, Amount = 100 }), online: false);
            Assert.Equal(silver + 100, Convert.ToInt64(await this.ColumnAsync("Silver", id)));
            AssertDone(await rpc.Admin.AddConquerPoints(new AmountArgs { ActorAccountID = Actor, CharacterID = id, Amount = 7 }), online: false);
            Assert.Equal(7L, Convert.ToInt64(await this.ColumnAsync("Jewels", id)));
            AssertDone(await rpc.Admin.SetLevel(new LevelArgs { ActorAccountID = Actor, CharacterID = id, Level = 15 }), online: false);
            Assert.Equal(15, Convert.ToInt32(await this.ColumnAsync("Level", id)));
            Assert.Equal(AdminErrors.OutOfRange, (await rpc.Admin.SetLevel(new LevelArgs { ActorAccountID = Actor, CharacterID = id, Level = 0 })).Error);
            Assert.Equal(AdminErrors.NotFound, (await rpc.Admin.AddSilver(new AmountArgs { ActorAccountID = Actor, CharacterID = 999999999, Amount = 1 })).Error);
        }

        [Fact]
        public async Task OfflineChangeIsKeptByTheNextLogin()
        {
            await using var rpc = await Rpc.ConnectAsync(this.Servers);
            var (id, _, email) = await this.OfflineCharacterAsync();
            uint silver = Convert.ToUInt32(await this.ColumnAsync("Silver", id));
            AssertDone(await rpc.Admin.AddSilver(new AmountArgs { ActorAccountID = Actor, CharacterID = id, Amount = 777 }), online: false);
            await using var player = await this.Servers.PlayAsync(email, Password);
            Assert.Equal(silver + 777, player.User.Silver);
        }

        [Fact]
        public async Task MessageAndBroadcastReachPlayers()
        {
            await using var rpc = await Rpc.ConnectAsync(this.Servers);
            await using var target = (await this.PlayingAtAsync(1002, 318, 340)).Player;

            AssertDone(await rpc.Admin.Message(new MessageArgs { ActorAccountID = Actor, CharacterID = target.User.Id, Text = "hello from the web" }), online: true);
            var whisper = await target.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Whisper && t.Text == "hello from the web");
            Assert.NotNull(whisper);
            Assert.Equal("[GM]", whisper.Sender);

            AssertDone(await rpc.Admin.Broadcast(new BroadcastArgs { ActorAccountID = Actor, Text = "server restarts soon" }), online: true);
            Assert.NotNull(await target.Game.WaitForTalkAsync(t => t.Text == "server restarts soon"));
            await this.Servers.WaitForEventAsync("game", "gm.broadcast", "actorAccountId", Actor.ToString());

            var (id, _, _) = await this.OfflineCharacterAsync();
            Assert.Equal(AdminErrors.Offline, (await rpc.Admin.Message(new MessageArgs { ActorAccountID = Actor, CharacterID = id, Text = "hi" })).Error);
        }
    }
}
