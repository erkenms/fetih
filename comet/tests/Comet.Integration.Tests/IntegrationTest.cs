namespace Comet.Integration.Tests
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    [Collection(ServerCollection.Name)]
    public abstract class IntegrationTest
    {
        // Messages the game server sends in MsgTalk
        public const string AnswerOk = "ANSWER_OK";
        public const string NewRole = "NEW_ROLE";
        public const string InvalidLogin = "Invalid login";
        public const string InvalidCharacter = "Invalid character";
        public const string InvalidName = "Invalid character name";
        public const string NameTaken = "Character name taken";

        // MsgConnectEx rejection codes
        public const uint WrongPassword = 1;
        public const uint RealmOffline = 10;
        public const uint Banned = 12;
        public const uint Locked = 22;
        public const uint RealmLocked = 70;

        // Authority as in the account_authority table: 1 player, 2 moderator, 3 game master
        public const ushort Moderator = 2, GameMaster = 3;

        public const string Password = "secret1";

        // How long to wait for a packet that, after SyncAsync, would already have come
        protected static readonly TimeSpan Already = TimeSpan.FromMilliseconds(50);

        protected readonly ServerFixture Servers;

        protected IntegrationTest(ServerFixture servers) => this.Servers = servers;

        protected async Task<(GameClient Game, ulong Token, string Email)> AtCharacterCreationAsync()
        {
            var email = await this.Servers.CreateAccountAsync(Password);
            var login = await this.Servers.LoginAsync(email, Password);
            Assert.True(login.Accepted, $"Login refused with code {login.RejectCode}");

            var game = await this.Servers.ConnectGameAsync();
            var result = await game.LoginAsync(login.Token);
            Assert.Equal(NewRole, result.Message);
            return (game, login.Token, email);
        }

        protected async Task<(Player Player, string Email)> PlayingAtAsync(
            uint mapID, int x, int y, ushort authority = 1, byte level = 1, ushort strength = 0, ushort mesh = 1003,
            byte currentClass = 0)
        {
            var (first, email) = await this.PlayingAsync(mesh: mesh);
            var name = first.User.Name;

            // After the logout save, or it could overwrite what's set below
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE `character` SET MapID = @map, X = @x, Y = @y, Level = @level WHERE Name = @name",
                ("@map", mapID), ("@x", x), ("@y", y), ("@level", level), ("@name", name));
            if (currentClass > 0)
                await this.Servers.ExecuteGameSqlAsync("UPDATE `character` SET CurrentClass = @class WHERE Name = @name",
                    ("@class", currentClass), ("@name", name));
            if (strength > 0)
                await this.Servers.ExecuteGameSqlAsync("UPDATE `character` SET Strength = @strength WHERE Name = @name",
                    ("@strength", strength), ("@name", name));
            await this.Servers.ExecuteAccountSqlAsync(
                "UPDATE account SET AuthorityID = @authority WHERE Username = @email",
                ("@authority", authority), ("@email", email));
            return (await this.Servers.PlayAsync(email, Password), email);
        }

        protected async Task<(Player Player, string Email)> PlayingAsync(
            ushort mesh = 1003, ushort profession = 10)
        {
            var email = await this.Servers.CreateAccountAsync(Password);
            var player = await this.Servers.PlayAsync(email, Password,
                new NewCharacter(this.Servers.NewCharacterName(), mesh, profession));
            return (player, email);
        }

        // Closes the player's connection and waits for this session's logout save: what it wrote,
        // with mapId, x and y
        protected async Task<JsonElement> LogOutAsync(Player player)
        {
            int mark = this.Servers.Mark();
            await player.DisposeAsync();
            return await this.Servers.WaitForLogoutSaveAsync(player.User.Name, mark);
        }

        protected async Task<(uint Map, int X, int Y)> SavedPositionAsync(Player player)
        {
            var save = await this.LogOutAsync(player);
            return (save.GetProperty("mapId").GetUInt32(), save.GetProperty("x").GetInt32(), save.GetProperty("y").GetInt32());
        }

        // Packets of one client are processed in order, so once the answer to this is in, so
        // is everything the server sent for the player's earlier packets
        protected static async Task SyncAsync(Player player)
        {
            await player.Game.QueryStatsAsync(player.User.Id);
            Assert.NotNull(await player.Game.WaitForStatsAsync(s => s.Id == player.User.Id));
        }

        // Whispers go out while the sender's packet is processed, so once the recipient has this
        // one, they also have what the sender's earlier packets sent them
        protected static async Task SyncAsync(Player sender, Player recipient)
        {
            var text = "sync " + Guid.NewGuid().ToString("N");
            await sender.Game.SayAsync(TalkChannels.Whisper, sender.User.Name, recipient.User.Name, text);
            Assert.NotNull(await recipient.Game.WaitForTalkAsync(t => t.Text == text));
        }

        protected static Task CommandAsync(Player player, string command) =>
            player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", command);

        // Needs a game master
        protected static async Task<ItemInfo> GiveAsync(Player player, uint type)
        {
            await CommandAsync(player, "/item " + type);
            var item = await player.Game.WaitForItemAsync(i => i.TypeId == type);
            Assert.NotNull(item);
            return item;
        }

        protected static Task<UserAttribMessage> SilverAsync(Player player, ulong silver) =>
            player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == silver);

        protected static async Task PrayAsync(Player player, uint stoneType)
        {
            var stone = await GiveAsync(player, stoneType);
            // The 5619 client sends slot 17 for a praying stone
            await player.Game.EquipAsync(stone.Id, 17);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, stone.Id));
        }

        protected static async Task FreePkAsync(Player player)
        {
            await player.Game.SendAsync(ActionMessage.Build(player.User.Id, ActionMessage.PkMode, command: 0));
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.PkMode, player.User.Id));
        }

        protected static async Task OpenTradeAsync(Player asker, Player other)
        {
            // Both must be on the map, which each one's last login step does in parallel
            Assert.NotNull(await asker.Game.WaitForSpawnAsync(other.User.Id));
            await asker.Game.TradeAsync(TradeMessage.Request, other.User.Id);
            Assert.NotNull(await other.Game.WaitForTradeAsync(t => t.Action == TradeMessage.Request && t.Data == asker.User.Id));
            await other.Game.TradeAsync(TradeMessage.Request, asker.User.Id);
            Assert.NotNull(await asker.Game.WaitForTradeAsync(t => t.Action == TradeMessage.ShowTable && t.Data == other.User.Id));
            Assert.NotNull(await other.Game.WaitForTradeAsync(t => t.Action == TradeMessage.ShowTable && t.Data == asker.User.Id));
        }
    }
}
