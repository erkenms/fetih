namespace Comet.Integration.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Xunit;

    public class PacketTraceTests : IntegrationTest
    {
        public PacketTraceTests(ServerFixture servers) : base(servers) { }

        private List<string> Trace(string server, long connId) => this.Servers.Events(server)
            .Where(e => e.TryGetProperty("event", out var ev) && ev.GetString().StartsWith("packet.") &&
                        e.TryGetProperty("hex", out _) &&
                        e.TryGetProperty("connId", out var c) && c.GetInt64() == connId)
            .Select(Describe)
            .ToList();

        private static string Describe(JsonElement e) =>
            e.GetProperty("event").GetString()[7..] + " " +
            (e.TryGetProperty("packetName", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString() : e.GetProperty("packetType").GetUInt16().ToString()) +
            (e.TryGetProperty("action", out var action) ? " " + action.GetRawText() : "");

        [Fact]
        public async Task GameLoginIsTracedInOrder()
        {
            var (player, _) = await this.PlayingAsync();
            await using (player)
            {
                var login = await this.Servers.WaitForEventAsync("game", "session.login", "name", player.User.Name);
                long connId = login.GetProperty("connId").GetInt64();

                // The client's LoginComplete (251) is the last packet of the login
                await this.Servers.WaitForEventAsync("game", "packet.in",
                    e => e.GetProperty("connId").GetInt64() == connId &&
                         e.TryGetProperty("action", out var a) && a.GetInt32() == 251);

                var trace = this.Trace("game", connId);

                // Key exchange first (no packet type), then a new account's login: creating
                // the character, logging in again on the same connection, entering the map.
                // The client asks for each login step on the echo of the one before, so what the
                // server sends after an echo (MsgMapInfo, DisplayGears) may be traced before or
                // after the next request. Each direction keeps its order, and each step's
                // request comes before its echo.
                var expected = new[]
                {
                    "in MsgConnect", "out MsgTalk",             // NEW_ROLE
                    "in MsgRegister", "out MsgTalk",            // ANSWER_OK
                    "in MsgConnect", "out MsgTalk", "out MsgUserInfo",
                    "out MsgPeerage",                           // nobility rank
                    "out MsgUserAttrib",                        // Stamina
                    "in MsgAction 74", "out MsgAction 74",      // LoginSpawn
                    "out MsgMapInfo",
                    "in MsgAction 75", "out MsgAction 75",      // LoginInventory, still empty
                    "out MsgItem 46",                           // DisplayGears
                    "in MsgAction 76", "out MsgAction 76",      // LoginRelationships, no friends yet
                    "in MsgAction 77", "out MsgAction 77",      // LoginProficiencies, none yet
                    "in MsgAction 78", "out MsgAction 78",      // LoginSpells, none yet
                    "in MsgAction 97",                          // LoginGuild: the empty guild window
                    "out MsgSyndicateAttributeInfo", "out MsgAction 97",
                    "in MsgAction 251"                          // LoginComplete
                };
                var steps = trace.Skip(2).Take(expected.Length).ToList();
                foreach (var direction in new[] { "in ", "out " })
                    Assert.Equal(expected.Where(p => p.StartsWith(direction)),
                        steps.Where(p => p.StartsWith(direction)));
                foreach (var step in new[] { 74, 75, 76, 77, 78, 97 })
                    Assert.True(steps.IndexOf($"in MsgAction {step}") < steps.IndexOf($"out MsgAction {step}"),
                        $"Step {step} echoed before it was asked for: {string.Join(", ", steps)}");
            }
        }

        [Fact]
        public async Task AccountLoginIsTraced()
        {
            var email = await this.Servers.CreateAccountAsync(Password);
            var login = await this.Servers.LoginAsync(email, Password);
            Assert.True(login.Accepted);
            // The connection that sent this account's name
            var name = Convert.ToHexString(Encoding.Latin1.GetBytes(email));
            var request = await this.Servers.WaitForEventAsync("account", "packet.in",
                e => e.GetProperty("packetType").GetUInt16() == 1124 &&
                     e.GetProperty("hex").GetString().Contains(name));
            long connId = request.GetProperty("connId").GetInt64();

            var answer = await this.Servers.WaitForEventAsync("account", "packet.out",
                e => e.GetProperty("connId").GetInt64() == connId &&
                     e.GetProperty("packetType").GetUInt16() == 1055);
            Assert.True(answer.TryGetProperty("accountId", out _));
            var trace = this.Trace("account", connId);
            Assert.Equal(new[]
            {
                "out MsgEncryptCode", "in MsgAccountSRP6", "out MsgLoginChallengeS",
                "in MsgLoginProofC", "out MsgConnectEx"
            }, trace);
        }
    }
}
