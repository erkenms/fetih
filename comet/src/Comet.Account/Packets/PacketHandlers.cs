namespace Comet.Account.Packets
{
    using System;
    using System.Collections.Generic;
    using Comet.Account.States;
    using Comet.Network.Packets;

    /// <summary>
    /// The packets the account server handles. Replay tests use this to check every
    /// packet the real client sent against what the server does with it.
    /// </summary>
    public static class PacketHandlers
    {
        private static readonly Dictionary<PacketType, Func<MsgBase<Client>>> Factories =
            new Dictionary<PacketType, Func<MsgBase<Client>>>
            {
                [PacketType.MsgAccountSRP6] = () => new MsgAccountSRP6(),
                [PacketType.MsgLoginProofC] = () => new MsgLoginProofC(),
            };

        /// <summary>A new message for a packet type, or null if it isn't handled.</summary>
        public static MsgBase<Client> Create(PacketType type) =>
            Factories.TryGetValue(type, out var create) ? create() : null;

        /// <summary>True if the server handles this packet type.</summary>
        public static bool Handles(PacketType type) => Factories.ContainsKey(type);
    }
}
