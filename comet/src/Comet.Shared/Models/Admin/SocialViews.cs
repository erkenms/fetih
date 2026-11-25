namespace Comet.Shared.Models.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    // The game's RPC methods for the web's social pages, next to IGameAdmin
    public interface IGameSocial
    {
        Task<GuildWarState> GuildWarStatus();
        // Not Ok when the war already runs, or doesn't
        Task<AdminResult> StartGuildWar(AdminArgs args);
        Task<AdminResult> EndGuildWar(AdminArgs args);
        Task<AdminResult> DisbandGuild(GuildArgs args);
        Task<AdminResult> DeleteBoardPost(BoardPostArgs args);
    }

    public class GuildWarState
    {
        public bool Active { get; set; }
        public uint HolderGuildID { get; set; }
        public uint WinnerGuildID { get; set; }
        public bool RewardClaimed { get; set; }
        public DateTime? WonAt { get; set; }
        // When the war runs on its own, in the server's local time, like "Saturday 20:00 to Sunday 20:00"
        public string Schedule { get; set; }
        public uint PoleLife { get; set; }
        public uint PoleMaxLife { get; set; }
        public uint LeftGateLife { get; set; }
        public uint RightGateLife { get; set; }
        public uint GateMaxLife { get; set; }
        public bool LeftGateOpen { get; set; }
        public bool RightGateOpen { get; set; }
        // The guilds with the most damage on the pole this war
        public List<GuildWarScore> Scores { get; set; } = new List<GuildWarScore>();
    }

    public class GuildWarScore
    {
        public uint GuildID { get; set; }
        public string Name { get; set; }
        public ulong Score { get; set; }
    }

    public class GuildArgs : AdminArgs
    {
        public uint GuildID { get; set; }
    }

    // A message board post is one per character and board channel (MsgTalk 2201-2205)
    public class BoardPostArgs : AdminArgs
    {
        public ushort Channel { get; set; }
        public uint CharacterID { get; set; }
    }
}
