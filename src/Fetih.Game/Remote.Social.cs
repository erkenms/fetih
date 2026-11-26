namespace Fetih.Game
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Chat;
    using Fetih.Game.Social;
    using Fetih.Shared.Models.Admin;
    using static Fetih.Game.Packets.MsgTalk;

    // Guild war, guilds and the message board for the web's social pages
    public partial class Remote : IGameSocial
    {
        public Task<GuildWarState> GuildWarStatus()
        {
            var (pole, left, right, leftOpen, rightOpen) = GuildWar.Structures();
            return Task.FromResult(new GuildWarState
            {
                Active = GuildWar.Active,
                HolderGuildID = GuildWar.HolderGuildID,
                WinnerGuildID = GuildWar.WinnerGuildID,
                RewardClaimed = GuildWar.RewardClaimed,
                WonAt = GuildWar.WonAt is DateTime wonAt ? DateTime.SpecifyKind(wonAt, DateTimeKind.Utc) : null,
                Schedule = GuildWar.ScheduleText,
                PoleLife = pole,
                PoleMaxLife = GuildWar.PoleMaxLife,
                LeftGateLife = left,
                RightGateLife = right,
                GateMaxLife = GuildWar.GateMaxLife,
                LeftGateOpen = leftOpen,
                RightGateOpen = rightOpen,
                Scores = GuildWar.TopScores(10)
                    .Select(s => new GuildWarScore { GuildID = s.GuildID, Name = s.Name, Score = s.Score })
                    .ToList(),
            });
        }

        // Like /guildwar start: the war then runs until ended by hand
        public async Task<AdminResult> StartGuildWar(AdminArgs args)
        {
            var result = await GuildWar.StartAsync(DateTime.UtcNow, manual: true)
                ? AdminResult.Done(false) : AdminResult.Fail(AdminErrors.Invalid);
            return LoggedSocial(LogEvents.GmGuildWar, args, "start", result);
        }

        public async Task<AdminResult> EndGuildWar(AdminArgs args)
        {
            var result = await GuildWar.EndAsync(DateTime.UtcNow, manual: true)
                ? AdminResult.Done(false) : AdminResult.Fail(AdminErrors.Invalid);
            return LoggedSocial(LogEvents.GmGuildWar, args, "end", result);
        }

        public async Task<AdminResult> DisbandGuild(GuildArgs args)
        {
            var result = await Guilds.DisbandAsync(args.GuildID, $"admin account {args.ActorAccountID}")
                ? AdminResult.Done(false) : AdminResult.Fail(AdminErrors.NotFound);
            return LoggedSocial(LogEvents.GmDisbandGuild, args, $"guild {args.GuildID}", result);
        }

        public async Task<AdminResult> DeleteBoardPost(BoardPostArgs args)
        {
            var channel = (TalkChannel)args.Channel;
            var result = !MessageBoard.IsBoardChannel(channel) ? AdminResult.Fail(AdminErrors.Invalid)
                : await MessageBoard.RemoveAsync(channel, args.CharacterID) ? AdminResult.Done(false)
                : AdminResult.Fail(AdminErrors.NotFound);
            return LoggedSocial(LogEvents.GmDeletePost, args, $"{channel} post of character {args.CharacterID}", result);
        }

        private static AdminResult LoggedSocial(string eventName, AdminArgs args, string detail, AdminResult result)
        {
            LogEvents.For<Remote>(eventName).Information(
                "Admin account {ActorAccountId} ran {Action} ({Detail}): {Result}",
                args.ActorAccountID, eventName.Substring("gm.".Length), detail, result.Ok ? "done" : result.Error);
            return result;
        }
    }
}
