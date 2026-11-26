namespace Fetih.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    public class MarriageTests : IntegrationTest
    {
        private const uint Market = 1036, LoveStone = 390, Starlit = 600055, MeteorTear = 1088002;
        private const uint ProposeCommand = 1067;
        private const ushort Male = 1003, Female = 2001;

        public MarriageTests(ServerFixture servers) : base(servers) { }

        private Task<object> SpouseInDbAsync(uint characterId) =>
            this.Servers.QueryGameScalarAsync(
                "SELECT Spouse FROM `character` WHERE CharacterID = @id", ("@id", characterId));

        [Fact]
        public async Task LoveStoneHandsOutTheProposalCommand()
        {
            await using var player = (await this.PlayingAtAsync(Market, 192, 194, mesh: Male)).Player;

            await player.Game.TalkToNpcAsync(LoveStone);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(2);

            var command = await player.Game.WaitForActionAsync(ActionMessage.ClientCommand, player.User.Id);
            Assert.Equal(ProposeCommand, command?.Command);
            Assert.NotNull(await player.Game.ReadDialogAsync());
        }

        [Fact]
        public async Task MarriagePersistsAndShowsInUserInfoObserveWhisperAndTradePartner()
        {
            var (husband, husbandEmail) = await this.PlayingAtAsync(Market, 192, 194, mesh: Male);
            await using var husbandSession = husband;
            await using var wife = (await this.PlayingAtAsync(Market, 193, 194, mesh: Female)).Player;
            await using var observer = (await this.PlayingAtAsync(Market, 194, 194, mesh: Male)).Player;

            // Propose and accept
            await husband.Game.CourtAsync(husband.User.Id, wife.User.Id, 192, 194);
            var proposal = await wife.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Court && i.Sender == husband.User.Id);
            Assert.NotNull(proposal);

            await wife.Game.MarryAsync(wife.User.Id, husband.User.Id, 193, 194);
            var husbandSpouse = await husband.Game.WaitForNameAsync(
                n => n.Action == NameMessage.SetSpouse && n.Data == husband.User.Id);
            var wifeSpouse = await wife.Game.WaitForNameAsync(
                n => n.Action == NameMessage.SetSpouse && n.Data == wife.User.Id);
            Assert.Equal(wife.User.Name, husbandSpouse?.Strings.FirstOrDefault());
            Assert.Equal(husband.User.Name, wifeSpouse?.Strings.FirstOrDefault());
            Assert.Equal(wife.User.Name, await this.SpouseInDbAsync(husband.User.Id));
            Assert.Equal(husband.User.Name, await this.SpouseInDbAsync(wife.User.Id));
            await this.Servers.WaitForEventAsync("game", "marriage.married", "proposer", husband.User.Name);

            // QuerySpouse and CharacterObservation return the real spouse name
            await observer.Game.QuerySpouseAsync(husband.User.Id);
            var queried = await observer.Game.WaitForNameAsync(
                n => n.Action == NameMessage.Spouse && n.Data == husband.User.Id);
            Assert.Equal(wife.User.Name, queried?.Strings.FirstOrDefault());

            await observer.Game.ObserveAsync(observer.User.Id, wife.User.Id);
            var observed = await observer.Game.WaitForNameAsync(
                n => n.Action == NameMessage.Spouse && n.Data == wife.User.Id);
            Assert.Equal(husband.User.Name, observed?.Strings.FirstOrDefault());

            // Whisper details include the spouse's name (field 5)
            await observer.Game.SendAsync(NameMessage.Build(NameMessage.WhisperDetails, husband.User.Name));
            var whisper = await observer.Game.WaitForNameAsync(n => n.Action == NameMessage.WhisperDetails);
            Assert.NotNull(whisper);
            var fields = whisper.Strings[1].Split(' ');
            Assert.Equal(wife.User.Name, fields[5]);

            // Trade buddy info includes the spouse's name
            await observer.Game.TradeBuddyAsync(TradeBuddyMessage.Request, husband.User.Id);
            Assert.NotNull(await husband.Game.WaitForTradeBuddyAsync(b => b.Action == TradeBuddyMessage.Request));
            await husband.Game.TradeBuddyAsync(TradeBuddyMessage.Request, observer.User.Id);
            Assert.NotNull(await observer.Game.WaitForTradeBuddyAsync(b => b.Action == TradeBuddyMessage.Add && b.Id == husband.User.Id));
            var partnerInfo = await observer.Game.AskPartnerInfoAsync(observer.User.Id, husband.User.Id);
            Assert.Equal(wife.User.Name, partnerInfo?.Spouse);

            // Relogin sends the spouse in MsgUserInfo
            await this.LogOutAsync(husband);
            await using var back = await this.Servers.PlayAsync(husbandEmail, Password);
            Assert.Equal(wife.User.Name, back.User.Spouse);
        }

        [Fact]
        public async Task SameGenderAndUnaskedMarriagesAreRejected()
        {
            await using var first = (await this.PlayingAtAsync(Market, 190, 194, mesh: Male)).Player;
            await using var second = (await this.PlayingAtAsync(Market, 191, 194, mesh: Male)).Player;
            await using var woman = (await this.PlayingAtAsync(Market, 192, 195, mesh: Female)).Player;

            await first.Game.CourtAsync(first.User.Id, second.User.Id, 190, 194);
            Assert.NotNull(await first.Game.WaitForTalkAsync(t => t.Text.Contains("same gender")));
            await SyncAsync(second);
            Assert.Null(await second.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Court, Already));

            await woman.Game.MarryAsync(woman.User.Id, first.User.Id, 192, 195);
            await SyncAsync(woman);
            Assert.Equal("None", await this.SpouseInDbAsync(woman.User.Id));
            Assert.Equal("None", await this.SpouseInDbAsync(first.User.Id));
        }

        [Fact]
        public async Task StarlitDivorcesOnlineAndOfflineSpousesForAMeteorTear()
        {
            var (husband, _) = await this.PlayingAtAsync(Market, 74, 173, GameMaster, mesh: Male);
            await using var husbandSession = husband;
            var (wife, wifeEmail) = await this.PlayingAtAsync(Market, 75, 173, GameMaster, mesh: Female);

            // Marry first
            await husband.Game.CourtAsync(husband.User.Id, wife.User.Id, 74, 173);
            Assert.NotNull(await wife.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Court));
            await wife.Game.MarryAsync(wife.User.Id, husband.User.Id, 75, 173);
            Assert.NotNull(await husband.Game.WaitForNameAsync(n => n.Action == NameMessage.SetSpouse && n.Data == husband.User.Id));
            Assert.NotNull(await wife.Game.WaitForNameAsync(n => n.Action == NameMessage.SetSpouse && n.Data == wife.User.Id));

            // Divorce without a Meteor Tear fails
            await husband.Game.TalkToNpcAsync(Starlit);
            Assert.NotNull(await husband.Game.ReadDialogAsync());
            await husband.Game.AnswerNpcAsync(1);
            Assert.NotNull(await husband.Game.ReadDialogAsync());
            await husband.Game.AnswerNpcAsync(2);
            var denied = await husband.Game.ReadDialogAsync();
            Assert.Contains("Meteor Tear", denied?[0].Text ?? string.Empty);
            Assert.Equal(wife.User.Name, await this.SpouseInDbAsync(husband.User.Id));

            // Give a Meteor Tear and divorce while the wife is online
            var tear = await GiveAsync(husband, MeteorTear);
            await husband.Game.TalkToNpcAsync(Starlit);
            Assert.NotNull(await husband.Game.ReadDialogAsync());
            await husband.Game.AnswerNpcAsync(1);
            Assert.NotNull(await husband.Game.ReadDialogAsync());
            await husband.Game.AnswerNpcAsync(2);
            Assert.NotNull(await husband.Game.ReadDialogAsync());

            Assert.NotNull(await husband.Game.WaitForItemActionAsync(ItemMessage.Remove, tear.Id));
            var husbandCleared = await husband.Game.WaitForNameAsync(
                n => n.Action == NameMessage.SetSpouse && n.Data == husband.User.Id);
            var wifeCleared = await wife.Game.WaitForNameAsync(
                n => n.Action == NameMessage.SetSpouse && n.Data == wife.User.Id);
            Assert.Equal("None", husbandCleared?.Strings.FirstOrDefault());
            Assert.Equal("None", wifeCleared?.Strings.FirstOrDefault());
            Assert.Equal("None", await this.SpouseInDbAsync(husband.User.Id));
            Assert.Equal("None", await this.SpouseInDbAsync(wife.User.Id));

            // Marry again, log the wife out, and divorce while she is offline
            await husband.Game.CourtAsync(husband.User.Id, wife.User.Id, 74, 173);
            Assert.NotNull(await wife.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Court));
            await wife.Game.MarryAsync(wife.User.Id, husband.User.Id, 75, 173);
            Assert.NotNull(await husband.Game.WaitForNameAsync(n => n.Action == NameMessage.SetSpouse && n.Data == husband.User.Id));
            Assert.NotNull(await wife.Game.WaitForNameAsync(n => n.Action == NameMessage.SetSpouse && n.Data == wife.User.Id));

            await this.LogOutAsync(wife);
            var secondTear = await GiveAsync(husband, MeteorTear);
            await husband.Game.TalkToNpcAsync(Starlit);
            Assert.NotNull(await husband.Game.ReadDialogAsync());
            await husband.Game.AnswerNpcAsync(1);
            Assert.NotNull(await husband.Game.ReadDialogAsync());
            await husband.Game.AnswerNpcAsync(2);
            Assert.NotNull(await husband.Game.ReadDialogAsync());
            Assert.NotNull(await husband.Game.WaitForItemActionAsync(ItemMessage.Remove, secondTear.Id));

            Assert.Equal("None", await this.SpouseInDbAsync(husband.User.Id));
            Assert.Equal("None", await this.SpouseInDbAsync(wife.User.Id));
            await using var wifeBack = await this.Servers.PlayAsync(wifeEmail, Password);
            Assert.Equal("None", wifeBack.User.Spouse);
        }
    }
}
