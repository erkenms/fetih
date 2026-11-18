namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <summary>
    /// The packets the game server handles. Packets that carry many actions (MsgAction,
    /// MsgItem) also say which of their actions are handled. Replay tests use this to
    /// check every packet the real client sent against what the server does with it.
    /// </summary>
    public static class PacketHandlers
    {
        private static readonly Dictionary<PacketType, Func<MsgBase<Client>>> Factories =
            new Dictionary<PacketType, Func<MsgBase<Client>>>
            {
                [PacketType.MsgRegister] = () => new MsgRegister(),
                [PacketType.MsgItem] = () => new MsgItem(),
                [PacketType.MsgAction] = () => new MsgAction(),
                [PacketType.MsgConnect] = () => new MsgConnect(),
            };

        /// <summary>A new message for a packet type, or null if it isn't handled.</summary>
        public static MsgBase<Client> Create(PacketType type) =>
            Factories.TryGetValue(type, out var create) ? create() : null;

        /// <summary>True if the server handles this packet type.</summary>
        public static bool Handles(PacketType type) => Factories.ContainsKey(type);

        /// <summary>
        /// True if the server handles this packet, including its action for MsgAction
        /// and MsgItem.
        /// </summary>
        /// <param name="type">Packet type</param>
        /// <param name="action">Action of a MsgAction or MsgItem (ignored for others)</param>
        public static bool Handles(PacketType type, uint action) => type switch
        {
            PacketType.MsgAction => MsgAction.Handles((MsgAction.ActionType)action),
            PacketType.MsgItem => MsgItem.Handles((MsgItem.ItemActionType)action),
            _ => Handles(type)
        };
    }
}
