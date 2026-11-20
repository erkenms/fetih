namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using Comet.Game.States;
    using Comet.Network.Packets;

    public static class PacketHandlers
    {
        private static readonly Dictionary<PacketType, Func<MsgBase<Client>>> Factories =
            new Dictionary<PacketType, Func<MsgBase<Client>>>
            {
                [PacketType.MsgRegister] = () => new MsgRegister(),
                [PacketType.MsgTalk] = () => new MsgTalk(),
                [PacketType.MsgItem] = () => new MsgItem(),
                [PacketType.MsgName] = () => new MsgName(),
                [PacketType.MsgAllot] = () => new MsgAllot(),
                [PacketType.MsgAction] = () => new MsgAction(),
                [PacketType.MsgConnect] = () => new MsgConnect(),
                [PacketType.MsgWalk] = () => new MsgWalk(),
                [PacketType.MsgMapItem] = () => new MsgMapItem(),
                [PacketType.MsgPackage] = () => new MsgPackage(),
                [PacketType.MsgNpc] = () => new MsgNpc(),
                [PacketType.MsgTaskDialog] = () => new MsgTaskDialog(),
                [PacketType.MsgTrade] = () => new MsgTrade(),
                [PacketType.MsgTeam] = () => new MsgTeam(),
                [PacketType.MsgFriend] = () => new MsgFriend(),
                [PacketType.MsgInteract] = () => new MsgInteract(),
                [PacketType.MsgSyndicate] = () => new MsgSyndicate(),
                [PacketType.MsgSynMemberList] = () => new MsgSynMemberList(),
                [PacketType.MsgSynpOffer] = () => new MsgSynpOffer(),
                [PacketType.MsgFamily] = () => new MsgFamily(),
            };

        public static MsgBase<Client> Create(PacketType type) =>
            Factories.TryGetValue(type, out var create) ? create() : null;

        public static bool Handles(PacketType type) => Factories.ContainsKey(type);

        public static bool Handles(PacketType type, uint action) => type switch
        {
            PacketType.MsgAction => MsgAction.Handles((MsgAction.ActionType)action),
            PacketType.MsgItem => MsgItem.Handles((MsgItem.ItemActionType)action),
            PacketType.MsgName => MsgName.Handles((MsgName.NameAction)action),
            _ => Handles(type)
        };
    }
}
