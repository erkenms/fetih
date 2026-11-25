namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comet.Core.Logging;
using Comet.Web.Features.Chat;
using Comet.Web.Features.Logs;
using Xunit;

[Collection(WebCollection.Name)]
public class ChatTests(WebFixture fixture)
{
    [Fact]
    public void ChatLogQlBuildsDefaultAndChannelQueries()
    {
        Assert.Equal(
            "{server=\"game\", event=~\"chat\\\\.(message|command)|gm\\\\.command\"}",
            ChatLogQl.BuildChatQuery(new ChatQueryFilter()));

        Assert.Equal(
            "{server=\"game\", event=\"chat.message\"} | json | channel=\"Whisper\"",
            ChatLogQl.BuildChatQuery(new ChatQueryFilter(Channel: "whisper")));

        Assert.Equal(
            "{server=\"game\", event=~\"chat\\\\.command|gm\\\\.command\"}",
            ChatLogQl.BuildChatQuery(new ChatQueryFilter(Channel: "Command")));
    }

    [Fact]
    public void ChatLogQlFiltersCharacterAccountAndWords()
    {
        var byId = ChatLogQl.BuildChatQuery(new ChatQueryFilter(
            Channel: "World",
            CharacterId: 1000042,
            AccountIds: [7],
            Words: "buy \"db\""));

        Assert.Equal(
            "{server=\"game\", event=\"chat.message\"} | json | channel=\"World\" | characterId=\"1000042\" | accountId=\"7\" |~ \"(?i)buy\\\\ \\\"db\\\"\"",
            byId);

        var byNameAndMultiAccount = ChatLogQl.BuildChatQuery(new ChatQueryFilter(
            CharacterName: "Alice",
            AccountIds: [3, 9]));

        Assert.Equal(
            "{server=\"game\", event=~\"chat\\\\.(message|command)|gm\\\\.command\"} | json | name=~\"(?i).*Alice.*\" | accountId=~\"^(3|9)$\"",
            byNameAndMultiAccount);
    }

    [Fact]
    public void GmCommandQueryFiltersCommandAndSender()
    {
        var query = ChatLogQl.BuildGmCommandQuery(new GmCommandQueryFilter(
            CharacterName: "GMTest",
            AccountIds: [2],
            Command: "/tp",
            Words: "1002"));

        Assert.Equal(
            "{server=\"game\", event=\"gm.command\"} | json | name=~\"(?i).*GMTest.*\" | accountId=\"2\" | command=~\"(?i).*tp.*\" |~ \"(?i)1002\"",
            query);
    }

    [Fact]
    public void ParseEntryExtractsWhisperTalkAndCommands()
    {
        var ts = new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

        var whisper = ChatLogQl.ParseEntry(new LogEntry(
            ts, "game", "info", LogEvents.ChatMessage,
            "Alice said on Whisper to Bob: hello", 1000001, 5, "Comet.Game.Chat.Talk", null,
            new Dictionary<string, string>
            {
                ["name"] = "Alice",
                ["channel"] = "Whisper",
                ["recipient"] = "Bob",
                ["text"] = "hello",
            }));
        Assert.Equal("Whisper", whisper.Channel);
        Assert.Equal("Alice", whisper.Sender);
        Assert.Equal("Bob", whisper.Recipient);
        Assert.Equal("hello", whisper.Text);
        Assert.Equal(1000001u, whisper.CharacterId);
        Assert.Equal(5u, whisper.AccountId);

        var talk = ChatLogQl.ParseEntry(new LogEntry(
            ts, "game", "info", LogEvents.ChatMessage,
            "Alice said on Talk to : hi", 1000001, 5, "Comet.Game.Chat.Talk", null,
            new Dictionary<string, string>
            {
                ["name"] = "Alice",
                ["channel"] = "Talk",
                ["recipient"] = "null",
                ["text"] = "hi",
            }));
        Assert.Equal("Talk", talk.Channel);
        Assert.Null(talk.Recipient);
        Assert.Equal("hi", talk.Text);

        var playerCmd = ChatLogQl.ParseEntry(new LogEntry(
            ts, "game", "info", LogEvents.ChatCommand,
            "Alice ran /online : ok", 1000001, 5, "Comet.Game.Chat.Commands", null,
            new Dictionary<string, string>
            {
                ["name"] = "Alice",
                ["command"] = "online",
                ["arguments"] = "",
                ["result"] = "ok",
            }));
        Assert.Equal("Command", playerCmd.Channel);
        Assert.Equal("/online", playerCmd.Text);
        Assert.Equal("online", playerCmd.Command);
        Assert.Equal("ok", playerCmd.Result);

        var gmCmd = ChatLogQl.ParseEntry(new LogEntry(
            ts, "game", "info", LogEvents.GmCommand,
            "Admin ran /tp 1002 400 400: ok", 1000002, 1, "Comet.Game.Chat.Commands", null,
            new Dictionary<string, string>
            {
                ["name"] = "Admin",
                ["command"] = "tp",
                ["arguments"] = "1002 400 400",
                ["result"] = "ok",
            }));
        Assert.Equal("GmCommand", gmCmd.Channel);
        Assert.Equal("/tp 1002 400 400", gmCmd.Text);
        Assert.Equal("tp", gmCmd.Command);
        Assert.Equal("1002 400 400", gmCmd.Arguments);
        Assert.Equal("ok", gmCmd.Result);
    }

    [Fact]
    public async Task ChatEndpointsEnforcePermissionsAndValidateRanges()
    {
        var anon = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/chat/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/chat/gm-commands")).StatusCode);

        var player = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/chat/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/chat/gm-commands")).StatusCode);

        string modEmail = await fixture.CreateAccountAsync(authority: 2);
        var moderator = await fixture.LoginAsync(modEmail, "secret1");

        var badRange = await moderator.GetAsync("/api/chat/messages?from=2026-11-25T12:00:00Z&to=2026-11-25T10:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, badRange.StatusCode);

        // Unmatched account name returns empty without needing Loki
        var noMatch = await moderator.GetAsync("/api/chat/messages?account=definitely_no_such_account_xyz");
        Assert.Equal(HttpStatusCode.OK, noMatch.StatusCode);
        var noMatchBody = await noMatch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, noMatchBody.GetProperty("entries").GetArrayLength());

        // Moderator does not have ViewAudit, so canViewAudit is false on gm-commands
        var modGm = await moderator.GetAsync("/api/chat/gm-commands");
        Assert.Equal(HttpStatusCode.OK, modGm.StatusCode);
        var modGmBody = await modGm.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(modGmBody.GetProperty("canViewAudit").GetBoolean());

        // GameMaster has ViewAudit, so canViewAudit is true on gm-commands
        string gmEmail = await fixture.CreateAccountAsync(authority: 3);
        var gm = await fixture.LoginAsync(gmEmail, "secret1");
        var gmRes = await gm.GetAsync("/api/chat/gm-commands");
        Assert.Equal(HttpStatusCode.OK, gmRes.StatusCode);
        var gmBody = await gmRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(gmBody.GetProperty("canViewAudit").GetBoolean());
    }
}
