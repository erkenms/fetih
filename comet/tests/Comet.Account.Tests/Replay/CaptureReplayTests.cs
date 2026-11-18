namespace Comet.Account.Tests.Replay
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Account.Packets;
    using Comet.Client;
    using Comet.Network.Packets;
    using Xunit;

    /// <summary>A fact that only runs when there are recorded captures of the real client.</summary>
    public sealed class AccountCaptureFactAttribute : FactAttribute
    {
        public AccountCaptureFactAttribute()
        {
            if (Captures.Load(Captures.FindDirectory(), "account").Count == 0)
                this.Skip = "No account captures in tests/Captures/5619 (see doc/Testing.md)";
        }
    }

    /// <summary>
    /// Replays every packet the real 5619 client sent to the account server in recorded
    /// VM sessions: each must be handled (and decode to its own length) or be listed as
    /// not handled yet.
    /// </summary>
    public class CaptureReplayTests
    {
        public static readonly Dictionary<ushort, string> NotHandledYet = new Dictionary<ushort, string>
        {
            [1100] = "MsgPCNum (sent after the login is accepted): to identify",
        };

        private static List<CapturedPacket> ClientPackets() =>
            Captures.Load(Captures.FindDirectory(), "account");

        [AccountCaptureFact]
        public void EveryClientPacketIsHandledOrListed()
        {
            var unknown = ClientPackets()
                .Where(p => !PacketHandlers.Handles((PacketType)p.Type) && !NotHandledYet.ContainsKey(p.Type))
                .GroupBy(p => p.Type)
                .Select(g => $"[{g.Key}] = \"...\", // {g.Count()}x, first: {g.First()}")
                .ToList();
            Assert.True(unknown.Count == 0,
                "The client sent packets the account server neither handles nor lists as not " +
                "handled yet. Handle them, or add them to NotHandledYet:\n" + string.Join("\n", unknown));
        }

        [AccountCaptureFact]
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
            var stale = NotHandledYet.Keys.Where(k => PacketHandlers.Handles((PacketType)k)).ToList();
            Assert.True(stale.Count == 0, "Handled now, remove from NotHandledYet: " + string.Join(", ", stale));
        }
    }
}
