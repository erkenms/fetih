namespace Comet.Network.Sockets
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using Comet.Core.Logging;
    using Comet.Network.Packets;

    public static class PacketTrace
    {
        private static volatile bool all;
        private static ImmutableHashSet<uint> accounts = ImmutableHashSet<uint>.Empty;
        private static ImmutableHashSet<string> addresses = ImmutableHashSet<string>.Empty;

        public static void Configure(PacketTraceConfiguration config)
        {
            config ??= new PacketTraceConfiguration();
            accounts = config.Accounts.ToImmutableHashSet();
            addresses = config.IPs.Select(ip => ip.Trim()).ToImmutableHashSet();
            all = config.All;
        }

        public static bool IsTraced(string ipAddress, uint accountID) =>
            all || addresses.Contains(ipAddress) || (accountID != 0 && accounts.Contains(accountID));

        public static bool IsAccountTraced(uint accountID) =>
            all || (accountID != 0 && accounts.Contains(accountID));

        public static void SetAccount(uint accountID, bool enabled)
        {
            if (accountID == 0)
                return;
            ImmutableInterlocked.Update(
                ref accounts,
                (set, state) => state.enabled ? set.Add(state.accountID) : set.Remove(state.accountID),
                (accountID, enabled));
        }

        public static void In(TcpServerActor actor, ReadOnlySpan<byte> packet)
        {
            PacketMetrics.In(packet);
            Write(actor, LogEvents.PacketIn, packet);
        }

        public static void Out(TcpServerActor actor, ReadOnlySpan<byte> packet)
        {
            PacketMetrics.Out(packet);
            Write(actor, LogEvents.PacketOut, packet);
        }

        private static void Write(TcpServerActor actor, string eventName, ReadOnlySpan<byte> packet)
        {
            if (!IsTraced(actor.IPAddress, actor.AccountID))
                return;

            ushort type = packet.Length >= 4 ? BitConverter.ToUInt16(packet[2..]) : (ushort)0;
            string name = Enum.IsDefined(typeof(PacketType), type) ? ((PacketType)type).ToString() : null;
            var logger = LogEvents.For(typeof(PacketTrace), eventName);
            if (actor.AccountID != 0)
                logger = logger.ForContext("AccountId", actor.AccountID);

            // The sub-type of the two packets that carry many actions, so a trace can be
            // filtered by action without decoding the hex
            if (type == (ushort)PacketType.MsgAction && packet.Length >= 22)
                logger = logger.ForContext("Action", BitConverter.ToUInt16(packet[20..]));
            else if (type == (ushort)PacketType.MsgItem && packet.Length >= 16)
                logger = logger.ForContext("Action", BitConverter.ToUInt32(packet[12..]));

            logger.Verbose(
                "{PacketName} ({PacketType}, {Length} bytes) on connection {ConnId}: {Hex}",
                name, type, packet.Length, actor.ConnId, Convert.ToHexString(packet));
        }
    }
}
