namespace Comet.Shared.Models.Admin
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    // The game server's admin RPC methods. The game implements it; a client can get a typed
    // proxy with StreamJsonRpc's JsonRpc.Attach<IGameAdmin>.
    public interface IGameAdmin
    {
        Task<ServerState> ServerStatus();
        Task<List<OnlinePlayer>> OnlinePlayers();
        // Null if there's no such map
        Task<MapState> MapSnapshot(uint mapID);
        // Null if there's no such character
        Task<CharacterState> Character(uint characterID);

        Task<AdminResult> Kick(KickArgs args);
        Task<AdminResult> Mute(MuteArgs args);
        Task<AdminResult> Jail(CharacterArgs args);
        Task<AdminResult> Ban(BanArgs args);
        Task<AdminResult> Unban(BanArgs args);
        Task<AdminResult> Teleport(TeleportArgs args);
        Task<AdminResult> GiveItem(GiveItemArgs args);
        Task<AdminResult> TakeItem(TakeItemArgs args);
        Task<AdminResult> AddSilver(AmountArgs args);
        Task<AdminResult> AddConquerPoints(AmountArgs args);
        Task<AdminResult> SetLevel(LevelArgs args);
        Task<AdminResult> Message(MessageArgs args);
        Task<AdminResult> Broadcast(BroadcastArgs args);
        Task<bool> IsPacketTraced(uint accountID);
        Task<AdminResult> SetPacketTrace(PacketTraceArgs args);
    }
}
