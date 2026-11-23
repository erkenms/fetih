namespace Comet.Game.Arena
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgQualifyingInteractive;

    // As MsgQualifyingDetailInfo shows it: the client's "Not Joined", "Waiting (Active)",
    // "Waiting (Inactive)" and "In Combat"
    public enum QualifierStatus : uint
    {
        NotSignedUp = 0,
        Waiting = 1,
        WaitingInactive = 2,
        Fighting = 3,
    }

    // The arena qualifier. Players sign up in the arena window and wait; the two waiting
    // longest with the closest arena points are asked to accept a match. When both accept
    // they're moved to a copy of map 900000 of their own; after a countdown they fight until
    // one dies, gives up or leaves, or time is up and the one who dealt more damage wins.
    // Everything is decided under one lock, from the players' packets and a loop each second;
    // what changes a player (teleports, health, items) runs on that player's queue.
    public static class Qualifier
    {
        public const uint MapID = 900000;

        // The client offers to raise the arena points to 1500
        public const uint PointsForSale = 1500;
        public const uint PointsPrice = 9_000_000;

        public const uint ExpBallType = 723700;

        public static TimeSpan InviteTime { get; set; } = TimeSpan.FromSeconds(60);
        public static TimeSpan CountdownTime { get; set; } = TimeSpan.FromSeconds(10);
        public static TimeSpan MatchTime { get; set; } = TimeSpan.FromMinutes(5);
        public static TimeSpan EndTime { get; set; } = TimeSpan.FromSeconds(3);

        private static readonly object Sync = new object();
        private static readonly Dictionary<uint, ArenaRecord> Records = new Dictionary<uint, ArenaRecord>();
        private static readonly List<uint> Waiting = new List<uint>();
        private static readonly List<ArenaMatch> Matches = new List<ArenaMatch>();
        private static uint CurrentDay;
        private static uint LastSeasonNumber;
        private static List<DbArenaSeason> LastSeason = new List<DbArenaSeason>();

        private sealed record Result(ArenaMatch Match, MatchState Before, Fighter Winner, Fighter Loser,
            uint Points, int WinnerBonuses, int LoserBonuses, DbArena WinnerSave, DbArena LoserSave);

        public static int Count
        {
            get { lock (Sync) return Records.Count; }
        }

        public static Task LoadAsync() => LoadAsync(DateTime.UtcNow);

        public static async Task LoadAsync(DateTime now)
        {
            var rows = await ArenaRepository.LoadAsync();
            var (lastSeasonNumber, lastSeason) = await ArenaRepository.LoadLastSeasonAsync();
            List<DbArenaSeason> closed = null;
            List<DbArena> saves = null;
            int rewarded = 0;
            uint today = ArenaRecord.DayOf(now);
            lock (Sync)
            {
                Records.Clear();
                foreach (var row in rows)
                    Records[row.Record.CharacterID] = new ArenaRecord(row.Record)
                    {
                        Name = row.Name, Level = row.Level, Class = row.Class, Mesh = row.Mesh,
                    };
                LastSeasonNumber = lastSeasonNumber;
                LastSeason = lastSeason;
                CurrentDay = today;

                // Close the latest past day that had matches if the server was offline across midnight
                uint pastDay = Records.Values
                    .Where(r => r.Stored.Day < today && r.MatchesToday > 0)
                    .Select(r => r.Stored.Day)
                    .DefaultIfEmpty(0u)
                    .Max();
                if (pastDay > 0)
                {
                    (closed, var awarded) = CloseSeasonLocked(pastDay);
                    rewarded = awarded.Count;
                }
                saves = ResetLocked(today, all: false);
            }
            await PersistSeasonAsync(closed, saves, rewarded);
        }

        public static async Task HandleAsync(Client client, MsgQualifyingInteractive msg)
        {
            if (client.Character == null)
                return;
            switch (msg.Action)
            {
                case QualifierAction.SignUp:
                    await SignUpAsync(client, msg);
                    break;
                case QualifierAction.Rejoin:
                    await SignUpAsync(client, new MsgQualifyingInteractive(QualifierAction.SignUp));
                    break;
                // From the win/lose dialog: sign up again
                case QualifierAction.EndDialog when msg.Option == 0:
                    await SignUpAsync(client, new MsgQualifyingInteractive(QualifierAction.SignUp));
                    break;
                case QualifierAction.Quit:
                    await QuitAsync(client, msg);
                    break;
                case QualifierAction.Answer when msg.Option == Accept:
                    await AcceptAsync(client);
                    break;
                case QualifierAction.Answer when msg.Option == GiveUp:
                    await ForfeitAsync(client, "gave_up");
                    break;
                case QualifierAction.OpponentGaveUp:
                    await ForfeitAsync(client, "gave_up");
                    break;
                case QualifierAction.BuyPoints:
                    await BuyPointsAsync(client);
                    break;
                default:
                    LogEvents.For(typeof(Qualifier), LogEvents.PacketUnhandled).Warning(
                        "Unhandled packet {PacketType} action {Action} option {Option} ({Length} bytes): {Hex}",
                        msg.Type, msg.Action, msg.Option, msg.Length, Convert.ToHexString(msg.Encode()));
                    break;
            }
        }

        public static async Task SendDetailsAsync(Client client)
        {
            var character = client.Character;
            if (character == null)
                return;
            var now = DateTime.UtcNow;
            Fighter fighter;
            QualifierStatus status;
            lock (Sync)
            {
                var record = RecordOf(character, now);
                fighter = record.ToFighter(RankOf(record.ID, ActiveDayLocked(now)));
                status = StatusOf(character.CharacterID);
            }
            await client.SendAsync(new MsgQualifyingDetailInfo(fighter, status));
        }

        public static async Task SendFightersAsync(Client client, uint page)
        {
            var now = DateTime.UtcNow;
            var list = new MsgQualifyingFightersList { Page = Math.Max(1, page) };
            lock (Sync)
            {
                var fighting = Matches.Where(m => m.InArena).ToList();
                list.Matches = fighting.Count;
                list.Waiting = Waiting.Count;
                foreach (var match in fighting.Skip((int)((list.Page - 1) * MsgQualifyingFightersList.PageSize))
                    .Take((int)MsgQualifyingFightersList.PageSize))
                    list.Shown.Add((FighterOf(match.A, now), FighterOf(match.B, now)));
            }
            await client.SendAsync(list);
        }

        public static async Task SendRankingAsync(Client client, MsgQualifyingRank.RankKind kind, ushort page)
        {
            var now = DateTime.UtcNow;
            var reply = new MsgQualifyingRank { Kind = kind, Page = Math.Max((ushort)1, page) };
            lock (Sync)
            {
                if (client.Character != null)
                    RecordOf(client.Character, now);
                var ranked = kind == MsgQualifyingRank.RankKind.Honor
                    ? ArenaRecord.RankByHonor(Records.Values)
                    : ArenaRecord.RankDay(Records.Values, ActiveDayLocked(now));
                reply.Total = ranked.Count;
                int first = (reply.Page - 1) * MsgQualifyingRank.PageSize;
                for (int i = first; i < ranked.Count && i < first + MsgQualifyingRank.PageSize; i++)
                    reply.Shown.Add(((uint)(i + 1), ranked[i].ToFighter((uint)(i + 1))));
            }
            await client.SendAsync(reply);
        }

        public static async Task SendSeasonRankAsync(Client client)
        {
            var reply = new MsgQualifyingSeasonRankList();
            lock (Sync)
                reply.Entries.AddRange(LastSeason);
            await client.SendAsync(reply);
        }

        public static async Task<(uint Season, int TopCount, int ResetCount, int Rewarded)> ResetSeasonAsync()
        {
            var now = DateTime.UtcNow;
            List<DbArenaSeason> top;
            List<DbArena> saves;
            List<(ArenaRecord Record, int Rank, uint Honor)> awarded;
            uint season;
            lock (Sync)
            {
                uint today = ArenaRecord.DayOf(now);
                if (CurrentDay == 0)
                    CurrentDay = today;
                (top, saves, awarded) = EndSeasonLocked(CurrentDay, today);
                season = LastSeasonNumber;
            }
            await PersistSeasonAsync(top, saves, awarded.Count);
            AnnounceHonor(awarded);
            return (season, top.Count, saves.Count, awarded.Count);
        }

        // Combat asks before a hit on a player: null when the arena has no say (the attacker
        // isn't in the arena), else whether these two are fighting each other now
        public static bool? CanFight(Client attacker, Client target)
        {
            if (attacker.Map?.Id != MapID)
                return null;
            lock (Sync)
            {
                var match = MatchOf(attacker.ID);
                match?.Fight(DateTime.UtcNow);
                return match != null && match.State == MatchState.Fighting && match.Has(target.ID) &&
                    attacker.Map == match.Map && target.Map == match.Map;
            }
        }

        // On the target's queue, after the damage was taken
        public static async Task OnHitAsync(Client attacker, Client target, uint damage)
        {
            if (damage == 0 || attacker.Map?.Id != MapID)
                return;
            MsgArenicScore score;
            lock (Sync)
            {
                var match = MatchOf(attacker.ID);
                if (match == null || match.State != MatchState.Fighting || !match.Has(target.ID))
                    return;
                match.Hit(attacker.ID, damage);
                score = ScoreOf(match);
            }
            await attacker.SendAsync(score);
            await target.SendAsync(score);
        }

        // True if the kill decided a match: then it's no PK
        public static async Task<bool> OnKillAsync(Client killer, Client victim)
        {
            if (killer.Map?.Id != MapID)
                return false;
            Result result;
            lock (Sync)
            {
                var match = MatchOf(killer.ID);
                if (match == null || !match.Has(victim.ID))
                    return false;
                result = EndLocked(match, killer.ID, "killed", DateTime.UtcNow);
            }
            if (result != null)
                await FinishAsync(result);
            return true;
        }

        public static bool IsSignedUp(Client client)
        {
            lock (Sync)
                return Waiting.Contains(client.ID) || MatchOf(client.ID) != null;
        }

        public static async Task LeaveAsync(Client client, string reason)
        {
            if (client.Character == null)
                return;
            Result result = null;
            lock (Sync)
            {
                Waiting.Remove(client.ID);
                var match = MatchOf(client.ID);
                if (match != null && match.State != MatchState.Ended)
                    result = EndLocked(match, match.Opponent(client.ID), reason, DateTime.UtcNow);
            }
            if (result != null)
                await FinishAsync(result);
        }

        private static async Task SignUpAsync(Client client, MsgQualifyingInteractive echo)
        {
            var character = client.Character;
            var now = DateTime.UtcNow;
            string reason;
            uint points;
            lock (Sync)
            {
                var record = RecordOf(character, now);
                points = record.Stored.Points;
                reason =
                    character.Level < ArenaRecord.MinLevel ? "level" :
                    InJail(character) ? "jail" :
                    Kernel.Maps?.Exists(MapID) != true ? "no_arena_map" :
                    MatchOf(client.ID) != null ? "in_match" :
                    points == 0 ? "no_points" :
                    null;
                if (reason == null && !Waiting.Contains(client.ID))
                    Waiting.Add(client.ID);
            }

            if (reason != null)
            {
                await RejectAsync(client, "sign_up", reason);
                return;
            }

            // The arena icon: the sign-up echoed
            echo.Option = 0;
            await client.SendAsync(echo);
            await SendDetailsAsync(client);
            LogEvents.For(typeof(Qualifier), LogEvents.ArenaJoined).Information(
                "{Name} signed up for the qualifier with {Points} arena points", character.Name, points);
            await PairAsync(now);
        }

        private static async Task QuitAsync(Client client, MsgQualifyingInteractive msg)
        {
            bool waited;
            Result result = null;
            lock (Sync)
            {
                waited = Waiting.Remove(client.ID);
                var match = MatchOf(client.ID);
                if (match != null && match.State != MatchState.Ended)
                    result = EndLocked(match, match.Opponent(client.ID), "quit", DateTime.UtcNow);
            }

            if (result != null)
            {
                await FinishAsync(result);
                return;
            }
            await client.SendAsync(msg);
            await SendDetailsAsync(client);
            if (waited)
                LogEvents.For(typeof(Qualifier), LogEvents.ArenaLeft).Information(
                    "{Name} stopped waiting for a qualifier match", client.Character.Name);
        }

        private static async Task AcceptAsync(Client client)
        {
            var now = DateTime.UtcNow;
            ArenaMatch match;
            bool start = false;
            string reason = null;
            lock (Sync)
            {
                match = MatchOf(client.ID);
                if (match == null || match.State != MatchState.Invited)
                    reason = "no_invitation";
                else if (match.Accept(client.ID, now, CountdownTime))
                {
                    try
                    {
                        match.Map = new GameMap<Client>(MapID, Kernel.Maps.Get(MapID));
                        start = true;
                    }
                    catch (Exception)
                    {
                        // MapStore logged why (map.load_failed)
                        Matches.Remove(match);
                        reason = "no_arena_map";
                    }
                }
            }

            if (reason != null)
            {
                await RejectAsync(client, "accept", reason);
                if (reason == "no_arena_map" && Kernel.Players.TryGet(match.Opponent(client.ID), out var opponent))
                    await RejectAsync(opponent, "accept", reason);
                return;
            }
            await SendDetailsAsync(client);
            if (!start)
                return;

            LogEvents.For(typeof(Qualifier), LogEvents.ArenaStarted).Information(
                "{Name} and {Opponent} start a qualifier match", NameOf(match.A), NameOf(match.B));
            foreach (var id in new[] { match.A, match.B })
                if (Kernel.Players.TryGet(id, out var fighter))
                    Kernel.Post(fighter, () => EnterAsync(fighter, match));
        }

        private static async Task ForfeitAsync(Client client, string reason)
        {
            Result result = null;
            lock (Sync)
            {
                var match = MatchOf(client.ID);
                if (match != null && match.State != MatchState.Ended)
                    result = EndLocked(match, match.Opponent(client.ID), reason, DateTime.UtcNow);
            }
            if (result == null)
            {
                await RejectAsync(client, "give_up", "no_match");
                return;
            }
            await FinishAsync(result);
        }

        private static async Task BuyPointsAsync(Client client)
        {
            var character = client.Character;
            uint before;
            lock (Sync)
                before = RecordOf(character, DateTime.UtcNow).Stored.Points;
            if (before >= PointsForSale)
            {
                await RejectAsync(client, "buy_points", "enough_points");
                return;
            }
            if (!await Progress.ChangeSilverAsync(client, -PointsPrice, "arena_points"))
            {
                await RejectAsync(client, "buy_points", "silver");
                return;
            }

            DbArena save;
            lock (Sync)
            {
                var record = RecordOf(character, DateTime.UtcNow);
                record.Stored.Points = Math.Max(record.Stored.Points, PointsForSale);
                save = record.Snapshot();
            }
            await SaveAsync(save);
            await SendDetailsAsync(client);
            LogEvents.For(typeof(Qualifier), LogEvents.ArenaPointsBought).Information(
                "{Name} bought arena points from {From} to {Points} for {Silver} silver",
                character.Name, before, save.Points, PointsPrice);
        }

        // On the fighter's queue
        private static async Task EnterAsync(Client client, ArenaMatch match)
        {
            var character = client.Character;
            if (character == null)
                return;
            bool first;
            lock (Sync)
            {
                if (match.State != MatchState.Countdown)
                    return;
                first = client.ID == match.A;
                match.SetReturn(client.ID, new MapPoint(character.MapID, character.X, character.Y));
            }

            if (client.Life.Dead)
                await Combat.Death.ReviveHereAsync(client, "qualifier");
            await Items.Riding.DismountAsync(client, "qualifier");
            var (x, y) = Spot(match.Map.Grid, first ? -1 : 1);
            if (!await Movement.TeleportAsync(client, match.Map, x, y))
            {
                await ForfeitAsync(client, "enter_failed");
                return;
            }

            Fighter opponent;
            MsgArenicScore score;
            bool closed;
            lock (Sync)
            {
                match.Enter(client.ID);
                opponent = FighterOf(match.Opponent(client.ID), DateTime.UtcNow);
                score = ScoreOf(match);
                closed = !Matches.Contains(match);
            }
            if (closed)
            {
                await LeaveArenaAsync(client, match);
                return;
            }

            await Restore(client);
            await client.SendAsync(new MsgQualifyingInteractive(QualifierAction.StartFight, 0, opponent));
            await client.SendAsync(new MsgQualifyingInteractive(QualifierAction.Match, MatchOn));
            await client.SendAsync(score);
            await SendDetailsAsync(client);
        }

        // On the fighter's queue once a match is over
        private static async Task LeaveArenaAsync(Client client, ArenaMatch match)
        {
            var character = client.Character;
            if (character == null)
                return;
            if (client.Map == match.Map)
            {
                if (client.Life.Dead)
                    await Combat.Death.ReviveHereAsync(client, "qualifier");
                var back = match.ReturnOf(client.ID);
                if (back == null || !await Movement.TeleportAsync(client, back.MapID, back.X, back.Y))
                    await Movement.TeleportAsync(client, StartPosition.MapID, StartPosition.X, StartPosition.Y);
                await Restore(client);
            }

            await client.SendAsync(new MsgArenicScore());
            await client.SendAsync(new MsgQualifyingInteractive(QualifierAction.Quit));
            await SendDetailsAsync(client);
            await client.SendAsync(new MsgQualifyingInteractive(QualifierAction.EndDialog,
                match.Winner == client.ID ? Won : 0));
        }

        private static async Task Restore(Client client)
        {
            var character = client.Character;
            character.HealthPoints = Attributes.MaxHealth(client);
            character.ManaPoints = Attributes.MaxMana(client);
            await client.SendAsync(new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.Health, character.HealthPoints));
            await client.SendAsync(new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.Mana, character.ManaPoints));
        }

        // A walkable cell near the middle of the arena, the fighters a step apart
        public static (ushort X, ushort Y) Spot(MapGrid grid, int offset)
        {
            int x0 = Math.Clamp(grid.Width / 2 + offset, 0, grid.Width - 1), y0 = grid.Height / 2;
            for (int r = 0; r < Math.Max(grid.Width, grid.Height); r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                            continue;
                        int x = x0 + dx, y = y0 + dy;
                        if (grid.Contains(x, y) && grid.IsWalkable(x, y))
                            return ((ushort)x, (ushort)y);
                    }
            return ((ushort)x0, (ushort)y0);
        }

        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try { await TickAsync(DateTime.UtcNow); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(Qualifier), LogEvents.ArenaLoopFailed).Error(e, "Qualifier round failed");
                }
            }
        }

        public static async Task TickAsync(DateTime now)
        {
            List<DbArenaSeason> closed = null;
            List<DbArena> seasonSaves = null;
            List<(ArenaRecord Record, int Rank, uint Honor)> awarded = null;
            var invited = new List<ArenaMatch>();
            var expired = new List<ArenaMatch>();
            var results = new List<Result>();
            var closing = new List<ArenaMatch>();
            lock (Sync)
            {
                uint today = ArenaRecord.DayOf(now);
                if (CurrentDay == 0)
                    CurrentDay = today;
                else if (today > CurrentDay)
                    (closed, seasonSaves, awarded) = EndSeasonLocked(CurrentDay, today);

                invited.AddRange(PairLocked(now));
                foreach (var match in Matches.ToList())
                {
                    switch (match.State)
                    {
                        case MatchState.Invited when now >= match.InvitedAt + InviteTime:
                            uint winner = match.UnansweredWinner();
                            if (winner != 0)
                                results.Add(EndLocked(match, winner, "no_answer", now));
                            else
                            {
                                Matches.Remove(match);
                                expired.Add(match);
                            }
                            break;
                        case MatchState.Countdown:
                        case MatchState.Fighting:
                            uint gone = GoneLocked(match);
                            if (gone != 0)
                            {
                                results.Add(EndLocked(match, match.Opponent(gone), "left", now));
                                break;
                            }
                            match.Fight(now);
                            if (match.State == MatchState.Fighting && now >= match.FightAt + MatchTime)
                                results.Add(EndLocked(match, match.WinnerByScore(Random.Shared.Next(2) == 0), "time_up", now));
                            break;
                        case MatchState.Ended when now >= match.EndedAt + EndTime:
                            Matches.Remove(match);
                            closing.Add(match);
                            break;
                    }
                }
            }

            if (closed != null || seasonSaves != null)
            {
                await PersistSeasonAsync(closed, seasonSaves, awarded.Count);
                AnnounceHonor(awarded);
            }
            foreach (var match in invited)
                await InviteAsync(match);
            foreach (var match in expired)
                await ExpireAsync(match);
            foreach (var result in results.Where(r => r != null))
                await FinishAsync(result);
            foreach (var match in closing)
                foreach (var id in new[] { match.A, match.B })
                    if (Kernel.Players.TryGet(id, out var fighter))
                        Kernel.Post(fighter, () => LeaveArenaAsync(fighter, match));
        }

        private static async Task PairAsync(DateTime now)
        {
            List<ArenaMatch> made;
            lock (Sync)
                made = PairLocked(now);
            foreach (var match in made)
                await InviteAsync(match);
        }

        public static List<(uint A, uint B)> Pairs(IReadOnlyList<(uint ID, uint Points)> waiting)
        {
            var left = waiting.ToList();
            var pairs = new List<(uint, uint)>();
            while (left.Count >= 2)
            {
                var first = left[0];
                var second = left.Skip(1).OrderBy(w => Math.Abs((long)w.Points - first.Points)).First();
                left.Remove(first);
                left.Remove(second);
                pairs.Add((first.ID, second.ID));
            }
            return pairs;
        }

        private static List<ArenaMatch> PairLocked(DateTime now)
        {
            Waiting.RemoveAll(id => !Kernel.Players.TryGet(id, out var client) || client.Character == null ||
                InJail(client.Character));
            var made = new List<ArenaMatch>();
            foreach (var (a, b) in Pairs(Waiting.Select(id => (id, Records[id].Stored.Points)).ToList()))
            {
                Waiting.Remove(a);
                Waiting.Remove(b);
                var match = new ArenaMatch(a, b, now);
                Matches.Add(match);
                made.Add(match);
            }
            return made;
        }

        private static async Task InviteAsync(ArenaMatch match)
        {
            var now = DateTime.UtcNow;
            LogEvents.For(typeof(Qualifier), LogEvents.ArenaMatched).Information(
                "{Name} and {Opponent} were matched in the qualifier", NameOf(match.A), NameOf(match.B));
            foreach (var id in new[] { match.A, match.B })
            {
                if (!Kernel.Players.TryGet(id, out var client))
                    continue;
                Fighter opponent;
                lock (Sync)
                    opponent = FighterOf(match.Opponent(id), now);
                await client.SendAsync(new MsgQualifyingInteractive(QualifierAction.Countdown, 0, opponent));
                await SendDetailsAsync(client);
            }
        }

        private static async Task ExpireAsync(ArenaMatch match)
        {
            LogEvents.For(typeof(Qualifier), LogEvents.ArenaLeft).Information(
                "{Name} and {Opponent} left the qualifier: neither accepted their match",
                NameOf(match.A), NameOf(match.B));
            foreach (var id in new[] { match.A, match.B })
            {
                if (!Kernel.Players.TryGet(id, out var client))
                    continue;
                await client.SendAsync(new MsgQualifyingInteractive(QualifierAction.Quit));
                await SendDetailsAsync(client);
                await Talk.NoticeAsync(client,
                    "You did not accept the challenge in time and were removed from the waiting list.");
            }
        }

        private static async Task FinishAsync(Result result)
        {
            var match = result.Match;
            await SaveAsync(result.WinnerSave);
            await SaveAsync(result.LoserSave);
            LogEvents.For(typeof(Qualifier), LogEvents.ArenaEnded).Information(
                "{Winner} beat {Loser} in the qualifier ({Reason}; damage {WinnerDamage} to {LoserDamage}); " +
                "{Points} arena points to {WinnerPoints}",
                result.Winner.Name, result.Loser.Name, match.Reason, match.ScoreOf(result.Winner.ID),
                match.ScoreOf(result.Loser.ID), result.Points, result.Winner.Points);

            Kernel.Players.TryGet(result.Winner.ID, out var winner);
            Kernel.Players.TryGet(result.Loser.ID, out var loser);
            if (result.Before == MatchState.Invited)
            {
                if (winner != null)
                {
                    await winner.SendAsync(new MsgQualifyingInteractive(QualifierAction.OpponentGaveUp));
                    await winner.SendAsync(new MsgQualifyingInteractive(QualifierAction.Quit));
                    await SendDetailsAsync(winner);
                    await winner.SendAsync(new MsgQualifyingInteractive(QualifierAction.EndDialog, Won));
                }
                if (loser != null)
                {
                    await loser.SendAsync(new MsgQualifyingInteractive(QualifierAction.Quit));
                    await SendDetailsAsync(loser);
                    await loser.SendAsync(new MsgQualifyingInteractive(QualifierAction.EndDialog, 0));
                }
            }
            else
            {
                if (winner != null)
                    await winner.SendAsync(new MsgQualifyingInteractive(QualifierAction.Dialog, Won));
                if (loser != null)
                    await loser.SendAsync(new MsgQualifyingInteractive(QualifierAction.Dialog, Lost));
            }

            if (winner != null && result.WinnerBonuses > 0)
                Kernel.Post(winner, () => GiveBonusesAsync(winner, result.WinnerBonuses));
            if (loser != null && result.LoserBonuses > 0)
                Kernel.Post(loser, () => GiveBonusesAsync(loser, result.LoserBonuses));

            var news = new MsgTalk(0, MsgTalk.TalkChannel.Qualifier,
                $"{result.Winner.Name} has defeated {result.Loser.Name} in the Qualifier and currently ranks {result.Winner.Rank}.");
            foreach (var player in Kernel.Players.Values.ToList())
                if (player.Character != null)
                    await player.SendAsync(news);
        }

        // On the player's queue: an EXP ball for each honor bonus
        private static async Task GiveBonusesAsync(Client client, int bonuses)
        {
            await Talk.NoticeAsync(client,
                $"You received {ArenaRecord.HonorBonus * bonuses} Honor Points in the Qualifier.");
            if (!Kernel.ItemTypes.TryGet(ExpBallType, out var ball))
                return;
            for (int i = 0; i < bonuses; i++)
                if (await Items.ItemActions.GiveAsync(client, ball, "qualifier") == null)
                    await Talk.NoticeAsync(client, "Your inventory is full; the EXP Ball is lost.");
        }

        private static async Task RejectAsync(Client client, string kind, string reason)
        {
            LogEvents.For(typeof(Qualifier), LogEvents.ArenaRejected).Information(
                "{Name} can't {Kind} in the qualifier: {Reason}", client.Character?.Name, kind, reason);
            string notice = reason switch
            {
                "level" => "Only level 40+ players can sign up for the Qualifier.",
                "no_points" => "You don't have enough Arena Points to sign up. Please pay silver to buy some.",
                "jail" => "You can't sign up for the Qualifier in jail.",
                "in_match" => "You are already in a Qualifier match.",
                "no_arena_map" => "The Qualifier is closed.",
                "silver" => "Sorry, you don't have enough silver.",
                "enough_points" => $"You can only buy Arena Points up to {PointsForSale}.",
                _ => null,
            };
            if (notice != null)
                await Talk.NoticeAsync(client, notice);
            await SendDetailsAsync(client);
        }

        private static async Task SaveAsync(DbArena record)
        {
            try { await ArenaRepository.SaveAsync(record); }
            catch (Exception e)
            {
                LogEvents.For(typeof(Qualifier), LogEvents.ArenaSaveFailed).Error(e,
                    "Couldn't save the arena record of character {CharacterId}", record.CharacterID);
            }
        }

        // Decides the match and settles both records; null if it was decided already. A match
        // decided before anyone entered the arena is gone at once.
        private static Result EndLocked(ArenaMatch match, uint winner, string reason, DateTime now)
        {
            var before = match.State;
            if (!match.End(winner, reason, now))
                return null;
            uint day = ActiveDayLocked(now);
            var winnerRecord = Records[winner];
            var loserRecord = Records[match.Opponent(winner)];
            var (points, winnerBonuses, loserBonuses) = ArenaRecord.Settle(winnerRecord, loserRecord, day);
            if (before == MatchState.Invited)
                Matches.Remove(match);
            return new Result(match, before,
                winnerRecord.ToFighter(RankOf(winnerRecord.ID, day)), loserRecord.ToFighter(RankOf(loserRecord.ID, day)),
                points, winnerBonuses, loserBonuses, winnerRecord.Snapshot(), loserRecord.Snapshot());
        }

        // A fighter who logged out or, once in the arena, isn't on its map any more
        private static uint GoneLocked(ArenaMatch match)
        {
            foreach (var id in new[] { match.A, match.B })
                if (!Kernel.Players.TryGet(id, out var client) || client.Character == null ||
                    match.Entered(id) && client.Map != match.Map)
                    return id;
            return 0;
        }

        private static uint ActiveDayLocked(DateTime now)
        {
            if (CurrentDay == 0)
                CurrentDay = ArenaRecord.DayOf(now);
            return CurrentDay;
        }

        private static (List<DbArenaSeason> Top, List<DbArena> Saves, List<(ArenaRecord Record, int Rank, uint Honor)> Awarded)
            EndSeasonLocked(uint endedDay, uint nextDay)
        {
            foreach (var record in Records.Values)
                RefreshLocked(record);
            var (top, awarded) = CloseSeasonLocked(endedDay);
            // The reset saves every record of the ended day, with the honor just awarded
            var saves = ResetLocked(nextDay, all: endedDay == nextDay);
            CurrentDay = nextDay;
            return (top, saves, awarded);
        }

        // Keeps the ended day's top ten as the latest season and pays each rank its honor
        private static (List<DbArenaSeason> Top, List<(ArenaRecord Record, int Rank, uint Honor)> Awarded)
            CloseSeasonLocked(uint endedDay)
        {
            var top = ArenaRecord.TopTen(Records.Values, endedDay, LastSeasonNumber + 1);
            if (top.Count > 0)
            {
                LastSeasonNumber++;
                LastSeason = top;
            }
            return (top, ArenaRecord.AwardSeasonHonor(Records.Values, endedDay, Kernel.HonorRewards));
        }

        private static void AnnounceHonor(List<(ArenaRecord Record, int Rank, uint Honor)> awarded)
        {
            foreach (var (record, rank, honor) in awarded)
                if (Kernel.Players != null && Kernel.Players.TryGet(record.ID, out var client) && client.Character != null)
                    Kernel.Post(client, () => Talk.NoticeAsync(client,
                        $"You ranked {rank} in the Qualifier season and received {honor} Honor Points."));
        }

        private static List<DbArena> ResetLocked(uint day, bool all)
        {
            var saves = new List<DbArena>();
            foreach (var record in Records.Values)
            {
                RefreshLocked(record);
                if (!all && record.Stored.Day == day)
                    continue;
                record.Reset(day);
                saves.Add(record.Snapshot());
            }
            return saves;
        }

        private static void RefreshLocked(ArenaRecord record)
        {
            if (Kernel.Players == null || !Kernel.Players.TryGet(record.ID, out var client) || client.Character == null)
                return;
            var character = client.Character;
            record.Name = character.Name;
            record.Level = character.Level;
            record.Class = character.CurrentClass;
            record.Mesh = (uint)(character.Mesh + character.Avatar * 10000);
        }

        private static async Task PersistSeasonAsync(List<DbArenaSeason> top, List<DbArena> saves, int rewarded)
        {
            bool hasTop = top != null && top.Count > 0;
            bool hasSaves = saves != null && saves.Count > 0;
            if (!hasTop && !hasSaves)
                return;
            try
            {
                if (hasTop)
                    await ArenaRepository.SaveSeasonAsync(top);
                if (hasSaves)
                    await ArenaRepository.SaveManyAsync(saves);
            }
            catch (Exception e)
            {
                LogEvents.For(typeof(Qualifier), LogEvents.ArenaSaveFailed).Error(e,
                    "Couldn't save the arena season reset");
                return;
            }
            LogEvents.For(typeof(Qualifier), LogEvents.ArenaSeasonReset).Information(
                "Arena season {Season} ended with {TopCount} ranked players; {RewardedCount} got honor; " +
                "reset {ResetCount} records",
                hasTop ? top[0].Season : LastSeasonNumber, hasTop ? top.Count : 0, rewarded,
                hasSaves ? saves.Count : 0);
        }

        private static ArenaRecord RecordOf(Character character, DateTime now)
        {
            uint day = ActiveDayLocked(now);
            if (!Records.TryGetValue(character.CharacterID, out var record))
                Records[character.CharacterID] = record = new ArenaRecord(new DbArena
                {
                    CharacterID = character.CharacterID,
                    Day = day,
                });
            record.Name = character.Name;
            record.Level = character.Level;
            record.Class = character.CurrentClass;
            record.Mesh = (uint)(character.Mesh + character.Avatar * 10000);
            record.Roll(day);
            record.GiveStartingPoints();
            return record;
        }

        private static Fighter FighterOf(uint id, DateTime now)
        {
            if (Kernel.Players.TryGet(id, out var client) && client.Character != null)
                RecordOf(client.Character, now);
            return Records.TryGetValue(id, out var record) ? record.ToFighter(RankOf(id, ActiveDayLocked(now))) : null;
        }

        private static uint RankOf(uint id, uint day)
        {
            int index = ArenaRecord.RankDay(Records.Values, day).FindIndex(r => r.ID == id);
            return (uint)(index + 1);
        }

        private static QualifierStatus StatusOf(uint id)
        {
            var match = MatchOf(id);
            if (match != null)
                return match.State == MatchState.Invited ? QualifierStatus.WaitingInactive : QualifierStatus.Fighting;
            return Waiting.Contains(id) ? QualifierStatus.Waiting : QualifierStatus.NotSignedUp;
        }

        private static ArenaMatch MatchOf(uint id) => Matches.FirstOrDefault(m => m.Has(id));

        private static MsgArenicScore ScoreOf(ArenaMatch match) => new MsgArenicScore
        {
            IdA = match.A,
            NameA = NameOf(match.A),
            DamageA = match.ScoreA,
            IdB = match.B,
            NameB = NameOf(match.B),
            DamageB = match.ScoreB,
        };

        private static string NameOf(uint id)
        {
            lock (Sync)
                return Records.TryGetValue(id, out var record) ? record.Name : id.ToString();
        }

        private static bool InJail(Character character) =>
            Kernel.MapTypes.Of(character.MapID).HasFlag(MapFlags.PrisonMap);
    }
}
