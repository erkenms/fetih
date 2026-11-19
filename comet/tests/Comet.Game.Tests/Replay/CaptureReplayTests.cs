namespace Comet.Game.Tests.Replay
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Client;
    using Comet.Game.Packets;
    using Comet.Network.Packets;
    using Xunit;

    /// <summary>A fact that only runs when there are recorded captures of the real client.</summary>
    public sealed class CaptureFactAttribute : FactAttribute
    {
        public CaptureFactAttribute()
        {
            if (Captures.Load(Captures.FindDirectory(), "game").Count == 0)
                this.Skip = "No game captures in tests/Captures/5619 (see doc/Testing.md)";
        }
    }

    /// <summary>
    /// Replays every packet the real 5619 client sent in recorded VM sessions against the
    /// game server's handlers. Each must either be handled (and decode to its own
    /// length) or be listed below as not handled yet. A packet or action that is neither
    /// fails the test, so a wrong assumption about what the client sends shows up the
    /// first time a capture contains it.
    /// </summary>
    public class CaptureReplayTests
    {
        /// <summary>
        /// Packets the client sends that the server doesn't handle yet, and why. An action of null means the whole packet type. Unhandled MsgActions
        /// are echoed, which is what the client waits for during login.
        /// </summary>
        public static readonly Dictionary<(ushort Type, uint? Action), string> NotHandledYet =
            new Dictionary<(ushort, uint?), string>
            {
                [(10010, 161)] = "MsgAction CharacterAway: away status",
                [(10010, 166)] = "MsgAction 166 (unknown, sent in game): to identify",
                [(10010, 75)] = "MsgAction LoginInventory: echoed until items exist",
                [(10010, 76)] = "MsgAction LoginRelationships: echoed until friends exist",
                [(10010, 77)] = "MsgAction LoginProficiencies: echoed until skills exist",
                [(10010, 78)] = "MsgAction LoginSpells: echoed until skills exist",
                [(10010, 96)] = "MsgAction CharacterPkMode: echoed until PK modes exist",
                [(10010, 97)] = "MsgAction LoginGuild: echoed until guilds exist",
                [(10010, 114)] = "MsgAction 114 (sent while logging in): to identify",
                [(1004, null)] = "MsgTalk: chat",
                [(1037, null)] = "MsgPing: to identify",
                [(1040, null)] = "1040 (unknown): to identify",
                [(1130, null)] = "MsgTitle: titles",
                [(1134, null)] = "MsgTaskStatus (sent while logging in): quests",
                [(1135, null)] = "MsgTaskDetailInfo: quests",
                [(1151, null)] = "1151 (unknown, sent while logging in): to identify",
                [(2206, null)] = "MsgQualifyingFightersList: arena",
                [(2208, null)] = "MsgQualifyingSeasonRankList: arena",
                [(2209, null)] = "MsgQualifyingDetailInfo: arena",
            };

        private static List<CapturedPacket> ClientPackets() =>
            Captures.Load(Captures.FindDirectory(), "game").Where(p => !p.IsKeyExchange).ToList();

        private static bool Listed(CapturedPacket p) =>
            NotHandledYet.ContainsKey((p.Type, p.Action)) || NotHandledYet.ContainsKey((p.Type, null));

        [CaptureFact]
        public void EveryClientPacketIsHandledOrListed()
        {
            var unknown = ClientPackets()
                .Where(p => !PacketHandlers.Handles((PacketType)p.Type, p.Action ?? 0) && !Listed(p))
                .GroupBy(p => (p.Type, p.Action))
                .Select(g => $"[({g.Key.Type}, {(g.Key.Action?.ToString() ?? "null")})] = \"...\", " +
                             $"// {g.Count()}x, first: {g.First()}")
                .ToList();
            Assert.True(unknown.Count == 0,
                "The client sent packets the server neither handles nor lists as not handled " +
                "yet. Handle them, or add them to NotHandledYet:\n" + string.Join("\n", unknown));
        }

        [CaptureFact]
        public void EveryHandledClientPacketDecodesToItsLength()
        {
            var failures = new List<string>();
            foreach (var packet in ClientPackets().Where(p => PacketHandlers.Handles((PacketType)p.Type)))
            {
                try
                {
                    var msg = PacketHandlers.Create((PacketType)packet.Type);
                    msg.Decode(packet.Bytes);
                    if (msg.Length != packet.Bytes.Length)
                        failures.Add($"{packet}: length field says {msg.Length}");
                }
                catch (Exception e)
                {
                    failures.Add($"{packet}: {e.GetType().Name} {e.Message}");
                }
            }

            Assert.True(failures.Count == 0, "Decoding failed:\n" + string.Join("\n", failures));
        }

        [Fact]
        public void NothingListedAsNotHandledIsHandled()
        {
            // Keeps the list honest when a packet or action gets a handler
            var stale = NotHandledYet.Keys
                .Where(k => k.Action == null
                    ? PacketHandlers.Handles((PacketType)k.Type)
                    : PacketHandlers.Handles((PacketType)k.Type, k.Action.Value))
                .Select(k => NotHandledYet[k])
                .ToList();
            Assert.True(stale.Count == 0, "Handled now, remove from NotHandledYet: " + string.Join(", ", stale));
        }
    }
}
