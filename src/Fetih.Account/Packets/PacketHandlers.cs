namespace Fetih.Account.Packets
{
    using System;
    using System.Collections.Generic;
    using Fetih.Account.States;
    using Fetih.Network.Packets;

    public static class PacketHandlers
    {
        private static readonly Dictionary<PacketType, Func<MsgBase<Client>>> Factories =
            new Dictionary<PacketType, Func<MsgBase<Client>>>
            {
                [PacketType.MsgAccountSRP6] = () => new MsgAccountSRP6(),
                [PacketType.MsgLoginProofC] = () => new MsgLoginProofC(),
            };

        public static MsgBase<Client> Create(PacketType type) =>
            Factories.TryGetValue(type, out var create) ? create() : null;

        public static bool Handles(PacketType type) => Factories.ContainsKey(type);
    }
}
