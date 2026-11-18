namespace Comet.Game.Tests.Replay
{
    using System;
    using System.IO;
    using System.Linq;
    using Comet.Client;
    using Xunit;

    public sealed class CapturesTests : IDisposable
    {
        private readonly string Directory =
            Path.Combine(Path.GetTempPath(), "comet-captures-" + Guid.NewGuid().ToString("N"));

        public CapturesTests() => System.IO.Directory.CreateDirectory(this.Directory);
        public void Dispose() => System.IO.Directory.Delete(this.Directory, recursive: true);

        private static string Line(string server, string ev, long connId, string hex) =>
            $"{{\"v\":1,\"level\":\"trace\",\"server\":\"{server}\",\"event\":\"{ev}\",\"connId\":{connId},\"hex\":\"{hex}\"}}";

        private static string Action(ushort action)
        {
            var bytes = Packet.Create(PacketTypes.MsgAction, 38);
            BitConverter.TryWriteBytes(bytes.AsSpan(20), action);
            return Convert.ToHexString(bytes);
        }

        [Fact]
        public void ReadsClientPacketsPerConnection()
        {
            File.WriteAllLines(Path.Combine(this.Directory, "session.jsonl"), new[]
            {
                Line("game", "packet.out", 1, "0000"),
                Line("game", "packet.in", 1, "AABBCCDD"),        // key exchange
                Line("game", "packet.in", 2, "AABBCCDD"),        // another connection's
                Line("game", "packet.in", 1, Action(74)),
                "{\"v\":1,\"event\":\"session.login\"}",
                Line("account", "packet.in", 7, "F0006404"),
                Line("game", "packet.in", 1, Action(251)),
            });

            var game = Captures.Load(this.Directory, "game");
            Assert.Equal(4, game.Count);
            Assert.True(game[0].IsKeyExchange);
            Assert.True(game[1].IsKeyExchange);
            Assert.Equal(new uint?[] { 74, 251 }, game.Where(p => p.ConnId == 1 && !p.IsKeyExchange).Select(p => p.Action));
            Assert.Equal(10010, game[2].Type);

            var account = Assert.Single(Captures.Load(this.Directory, "account"));
            Assert.Equal(1124, account.Type);
            Assert.False(account.IsKeyExchange);
        }

        [Fact]
        public void ConnectionsAreCountedAgainAfterARestart()
        {
            File.WriteAllLines(Path.Combine(this.Directory, "restart.jsonl"), new[]
            {
                Line("game", "packet.in", 1, "AABBCCDD"),
                Line("game", "packet.in", 1, Action(74)),
                "{\"v\":1,\"server\":\"game\",\"event\":\"server.start\"}",
                Line("game", "packet.in", 1, "AABBCCDD"),
            });

            var game = Captures.Load(this.Directory, "game");
            Assert.Equal(new[] { true, false, true }, game.Select(p => p.IsKeyExchange));
            Assert.NotEqual(game[0].ConnId, game[2].ConnId);
        }

        [Fact]
        public void NoDirectoryMeansNoPackets()
        {
            Assert.Empty(Captures.Load(null, "game"));
        }
    }
}
