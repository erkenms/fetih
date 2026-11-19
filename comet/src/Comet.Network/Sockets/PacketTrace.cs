namespace Comet.Network.Sockets
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using Comet.Core.Logging;
    using Comet.Network.Packets;

    /// <summary>
    /// Logs every packet of the traced connections: packet.in after decryption, and
    /// packet.out before encryption, with the raw bytes in hex (without the footer).
    /// Which connections are traced comes from Logging:PacketTrace (everyone, some
    /// account IDs or some IP addresses). Events are trace level and go to the JSON
    /// files only, whatever the configured level.
    /// </summary>
    public static class PacketTrace
    {
        private static volatile bool all;
        private static ImmutableHashSet<uint> accounts = ImmutableHashSet<uint>.Empty;
        private static ImmutableHashSet<string> addresses = ImmutableHashSet<string>.Empty;

        /// <summary>Sets which connections are traced.</summary>
        public static void Configure(PacketTraceConfiguration config)
        {
            config ??= new PacketTraceConfiguration();
            accounts = config.Accounts.ToImmutableHashSet();
            addresses = config.IPs.Select(ip => ip.Trim()).ToImmutableHashSet();
            all = config.All;
        }

        /// <summary>True if a connection from this address and account is traced.</summary>
        /// <param name="ipAddress">Client IP address</param>
        /// <param name="accountID">Account ID, or 0 before the account is known</param>
        public static bool IsTraced(string ipAddress, uint accountID) =>
            all || addresses.Contains(ipAddress) || (accountID != 0 && accounts.Contains(accountID));

        /// <summary>Logs a packet received from the client (decrypted, without footer).</summary>
        public static void In(TcpServerActor actor, ReadOnlySpan<byte> packet) =>
            Write(actor, LogEvents.PacketIn, packet);

        /// <summary>Logs a packet about to be sent to the client (before encryption).</summary>
        public static void Out(TcpServerActor actor, ReadOnlySpan<byte> packet) =>
            Write(actor, LogEvents.PacketOut, packet);

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
