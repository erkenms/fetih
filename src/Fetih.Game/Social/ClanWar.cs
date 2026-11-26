namespace Fetih.Game.Social
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Chat;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Database.Repositories;
    using Fetih.Game.Items;
    using Fetih.Game.Npcs;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Game.World;
    using static Fetih.Game.Packets.MsgFamilyOccupy;

    // The daily Clan War over ten maps, one per clan banner (role 31 NPCs in two maps of each
    // main city). Clan leaders sign up at a banner from 21:00 until 20:00 the next day for one
    // map at a time, paying the fee in silver. At 20:30 every map with sign-ups gets its own
    // battlefield (map 3024) and the members of the signed-up clans and of the occupying clan
    // are invited; they can enter until 20:35. On the battlefield clans fight each other; the
    // dead revive outside. The war on a map ends when at most one clan is left on it, or at
    // 21:00. The clan with the most kills wins, the occupier on a tie; a clan already holding
    // another map can't take this one. Occupiers count days and their leader claims a reward
    // pack once a day, a better one every seventh day.
    public static class ClanWar
    {
        public const uint BattlefieldMapID = 3024;
        public static readonly (ushort X, ushort Y) DefenderSpot = (70, 76);
        public static readonly (ushort X, ushort Y) AttackerSpot = (100, 120);

        // Asker's role in the occupy dialog
        public const uint RoleMember = 0, RoleLeader = 1, RoleOther = 2, RoleSignedUp = 3, RoleCanApply = 5;

        // Each city's packs run Super, Elite, Unique, Refined, Normal from its base type
        public sealed record Territory(uint NpcID, string Name, uint PackBase)
        {
            public uint Pack(int grade) => this.PackBase + (uint)grade;
        }

        public const int Super = 0, Elite = 1, Unique = 2, Refined = 3, Normal = 4;

        public static readonly IReadOnlyList<Territory> Territories = new[]
        {
            new Territory(101887, "Twin City", 722454),
            new Territory(101889, "Wind Plain", 722454),
            new Territory(101903, "Desert City", 722459),
            new Territory(101905, "Desert", 722459),
            new Territory(101899, "Bird City", 722464),
            new Territory(101901, "Bird Island", 722464),
            new Territory(101895, "Ape City", 722469),
            new Territory(101897, "Love Canyon", 722469),
            new Territory(101891, "Phoenix Castle", 722474),
            new Territory(101893, "Maple Forest", 722474),
        };

        public static int StartHour { get; set; } = 20;
        public static int StartMinute { get; set; } = 30;
        public static int EndHour { get; set; } = 21;
        public static int EndMinute { get; set; } = 0;
        public static int SignUpCloseHour { get; set; } = 20;
        public static int SignUpCloseMinute { get; set; } = 0;
        public static TimeSpan EntryTime { get; set; } = TimeSpan.FromMinutes(5);
        public static ulong Fee { get; set; } = 500_000;

        private sealed class Occupation
        {
            public uint ClanID;
            public uint Days;
            public bool Claimed;
        }

        private sealed class War
        {
            public Territory Territory;
            public GameMap<Client> Map;
            public uint DefenderID;
            public HashSet<uint> Clans = new HashSet<uint>();
            public Dictionary<uint, int> Kills = new Dictionary<uint, int>();
            public Dictionary<uint, MapPoint> Returns = new Dictionary<uint, MapPoint>();
            public bool Over;
        }

        public sealed record Result(uint NpcID, uint WinnerID, uint PreviousID, bool Taken);

        private static readonly object Sync = new object();
        private static readonly Dictionary<uint, Occupation> Occupied = new Dictionary<uint, Occupation>();
        private static readonly Dictionary<uint, uint> Signups = new Dictionary<uint, uint>();
        private static readonly Dictionary<uint, War> Wars = new Dictionary<uint, War>();
        private static bool IsActive;
        private static bool? ManualOverride;
        private static DateTime StartedAt;

        public static bool Active { get { lock (Sync) return IsActive; } }

        public static Territory Find(uint npcID) => Territories.FirstOrDefault(t => t.NpcID == npcID);

        public static uint OccupierOf(uint npcID)
        {
            lock (Sync)
                return Occupied.TryGetValue(npcID, out var o) ? o.ClanID : 0;
        }

        public static uint DaysOf(uint npcID)
        {
            lock (Sync)
                return Occupied.TryGetValue(npcID, out var o) ? o.Days : 0;
        }

        public static uint SignupOf(uint clanID)
        {
            lock (Sync)
                return Signups.GetValueOrDefault(clanID);
        }

        // Minutes since local midnight
        private static int Minute(int hour, int minute) => Math.Clamp(hour, 0, 23) * 60 + Math.Clamp(minute, 0, 59);

        private static bool Between(int now, int from, int to) =>
            from == to ? false : from < to ? now >= from && now < to : now >= from || now < to;

        public static bool IsWarTime(DateTime localNow) =>
            Between(localNow.Hour * 60 + localNow.Minute, Minute(StartHour, StartMinute), Minute(EndHour, EndMinute));

        // Sign-up is closed from the close time until the war's end
        public static bool IsSignUpOpen(DateTime localNow) =>
            !Between(localNow.Hour * 60 + localNow.Minute, Minute(SignUpCloseHour, SignUpCloseMinute), Minute(EndHour, EndMinute));

        // The pack the occupier's leader claims after the given number of days: a better one
        // every seventh day (Refined, Unique, Elite, Super, then again), else a Normal one
        public static uint RevenueOf(Territory territory, uint days)
        {
            if (days == 0 || days % 7 != 0)
                return territory.Pack(Normal);
            return territory.Pack(Refined - (int)((days / 7 - 1) % 4));
        }

        public static uint NextSpecialOf(Territory territory, uint days) =>
            RevenueOf(territory, (days / 7 + 1) * 7);

        public static async Task LoadAsync()
        {
            var (occupations, signups) = await ClanWarRepository.LoadAsync();
            lock (Sync)
            {
                Occupied.Clear();
                Signups.Clear();
                foreach (var row in occupations)
                    if (Find(row.NpcID) != null && Clans.Find(row.FamilyID) != null)
                        Occupied[row.NpcID] = new Occupation { ClanID = row.FamilyID, Days = row.Days, Claimed = row.RevenueClaimed };
                foreach (var row in signups)
                    if (Find(row.NpcID) != null && Clans.Find(row.FamilyID) != null)
                        Signups[row.FamilyID] = row.NpcID;
            }
            LogEvents.For(typeof(ClanWar), LogEvents.ClanWarLoaded).Information(
                "Loaded {OccupiedCount} occupied clan war maps and {SignupCount} sign-ups",
                Occupied.Count, Signups.Count);
        }

        public static async Task HandleAsync(Client client, MsgFamilyOccupy msg)
        {
            if (client.Character == null)
                return;
            switch (msg.Action)
            {
                case OccupyAction.Query: await QueryAsync(client, msg.NpcID); break;
                case OccupyAction.Apply: await ApplyAsync(client, msg.NpcID, again: false); break;
                case OccupyAction.ApplyAgain: await ApplyAsync(client, msg.NpcID, again: true); break;
                case OccupyAction.Cancel: await CancelAsync(client, msg.NpcID); break;
                case OccupyAction.GiveUp: await GiveUpAsync(client, msg.NpcID); break;
                case OccupyAction.Join: await JoinAsync(client, msg.NpcID); break;
                case OccupyAction.ClaimRevenue: await ClaimAsync(client, msg.NpcID); break;
                default:
                    LogEvents.For(typeof(ClanWar), LogEvents.PacketUnhandled).Warning(
                        "Unhandled packet {PacketType} action {Action} ({Length} bytes): {Hex}",
                        msg.Type, msg.Action, msg.Length, Convert.ToHexString(msg.Encode()));
                    break;
            }
        }

        public static MsgFamilyOccupy DialogFor(Client client, Territory territory, DateTime now)
        {
            var membership = Clans.Of(client.ID);
            bool leader = membership?.Member.Rank == ClanRank.Leader;
            var local = Clock.Local(now);
            lock (Sync)
            {
                Occupied.TryGetValue(territory.NpcID, out var occupation);
                var occupier = occupation == null ? null : Clans.Find(occupation.ClanID);
                uint days = occupation?.Days ?? 0;
                uint role =
                    occupier != null && membership?.Clan.ID == occupier.ID ? (leader ? RoleLeader : RoleMember) :
                    leader && Signups.GetValueOrDefault(membership.Clan.ID) == territory.NpcID ? RoleSignedUp :
                    leader ? RoleCanApply :
                    RoleOther;
                Wars.TryGetValue(territory.NpcID, out var war);
                return new MsgFamilyOccupy(OccupyAction.Query, territory.NpcID)
                {
                    Role = role,
                    Occupier = occupier?.Name ?? string.Empty,
                    MapName = territory.Name,
                    CanJoin = war != null && !war.Over && now < StartedAt + EntryTime &&
                        membership != null && war.Clans.Contains(membership.Clan.ID),
                    CanApply = !IsActive && IsSignUpOpen(local) && role == RoleCanApply,
                    CanChange = !IsActive,
                    Days = days,
                    DailyPrize = RevenueOf(territory, days),
                    WeeklyPrize = NextSpecialOf(territory, days),
                    Challenged = Signups.ContainsValue(territory.NpcID),
                    Fee = Fee,
                    RevenueUnclaimed = occupation != null && !occupation.Claimed,
                };
            }
        }

        // The clan window's war line: "challenged-map days pack next-pack challenged income exp",
        // then the occupied and the challenged map's names
        public static (string Numbers, string Occupied, string Challenged) InfoOf(uint clanID)
        {
            lock (Sync)
            {
                var held = Occupied.FirstOrDefault(o => o.Value.ClanID == clanID);
                var heldTerritory = held.Value == null ? null : Find(held.Key);
                var challenged = Find(Signups.GetValueOrDefault(clanID));
                uint days = held.Value?.Days ?? 0;
                string numbers = string.Join(' ',
                    challenged?.NpcID ?? 0,
                    days,
                    heldTerritory == null ? 0 : RevenueOf(heldTerritory, days),
                    heldTerritory == null ? 0 : NextSpecialOf(heldTerritory, days),
                    heldTerritory != null && Signups.ContainsValue(heldTerritory.NpcID) ? 1 : 0,
                    held.Value != null && held.Value.Claimed ? 1 : 0,
                    0);
                return (numbers, heldTerritory?.Name ?? string.Empty, challenged?.Name ?? string.Empty);
            }
        }

        private static async Task QueryAsync(Client client, uint npcID)
        {
            var territory = Reach(client, npcID, "query");
            if (territory == null)
                return;
            await client.SendAsync(DialogFor(client, territory, DateTime.UtcNow));
        }

        private static async Task ApplyAsync(Client client, uint npcID, bool again)
        {
            var territory = Reach(client, npcID, "apply");
            if (territory == null)
                return;
            var membership = Clans.Of(client.ID);
            var now = DateTime.UtcNow;
            uint previous;
            string reason;
            lock (Sync)
            {
                previous = membership == null ? 0 : Signups.GetValueOrDefault(membership.Clan.ID);
                reason =
                    membership == null ? "no_clan" :
                    membership.Member.Rank != ClanRank.Leader ? "not_leader" :
                    IsActive || !IsSignUpOpen(Clock.Local(now)) ? "closed" :
                    Occupied.TryGetValue(npcID, out var o) && o.ClanID == membership.Clan.ID ? "own_map" :
                    previous == npcID ? "signed_up" :
                    previous != 0 && !again ? "elsewhere" :
                    client.Character.Silver < Fee ? "silver" :
                    null;
            }
            if (reason == "elsewhere")
            {
                await client.SendAsync(new MsgFamilyOccupy(OccupyAction.AskReapply, npcID) { MapName = Find(previous)?.Name });
                return;
            }
            if (reason != null)
            {
                Reject(client, "apply", territory, reason);
                if (reason == "not_leader")
                    await Talk.NoticeAsync(client, "Only the Clan Leader can apply.");
                else if (reason == "silver")
                    await Talk.NoticeAsync(client, "You don't have enough silver.");
                return;
            }
            if (Fee > 0 && !await Progress.ChangeSilverAsync(client, -(long)Fee, "clan_war_fee"))
            {
                Reject(client, "apply", territory, "silver");
                return;
            }

            var clan = membership.Clan;
            lock (Sync)
                Signups[clan.ID] = npcID;
            await ClanWarRepository.SaveSignupAsync(new DbFamilyWarSignup { FamilyID = clan.ID, NpcID = npcID });
            LogEvents.For(typeof(ClanWar), LogEvents.ClanWarSignedUp).Information(
                "{Name} signed clan {Clan} up for the clan war of {Map} (clan {ClanId}, npc {NpcId}, fee {Fee})",
                client.Character.Name, clan.Name, territory.Name, clan.ID, npcID, Fee);
            await client.SendAsync(DialogFor(client, territory, now));
            await Clans.RefreshInfoAsync(client);
        }

        private static async Task CancelAsync(Client client, uint npcID)
        {
            var membership = Clans.Of(client.ID);
            var territory = Find(npcID);
            string reason;
            lock (Sync)
                reason =
                    territory == null ? "unknown_map" :
                    membership?.Member.Rank != ClanRank.Leader ? "not_leader" :
                    IsActive ? "war_active" :
                    Signups.GetValueOrDefault(membership.Clan.ID) != npcID ? "not_signed_up" :
                    null;
            if (reason != null)
            {
                Reject(client, "cancel", territory, reason);
                if (reason == "not_leader")
                    await Talk.NoticeAsync(client, "Only the Clan Leader can remove the application.");
                return;
            }
            lock (Sync)
                Signups.Remove(membership.Clan.ID);
            await ClanWarRepository.RemoveSignupAsync(membership.Clan.ID);
            LogEvents.For(typeof(ClanWar), LogEvents.ClanWarCancelled).Information(
                "{Name} withdrew clan {Clan} from the clan war of {Map} (clan {ClanId}, npc {NpcId})",
                client.Character.Name, membership.Clan.Name, territory.Name, membership.Clan.ID, npcID);
            if (Kernel.Npcs.Find(client.Character.MapID, npcID) != null)
                await client.SendAsync(DialogFor(client, territory, DateTime.UtcNow));
            await Clans.RefreshInfoAsync(client);
        }

        private static async Task GiveUpAsync(Client client, uint npcID)
        {
            var territory = Reach(client, npcID, "give_up");
            if (territory == null)
                return;
            var membership = Clans.Of(client.ID);
            string reason;
            lock (Sync)
                reason =
                    membership?.Member.Rank != ClanRank.Leader ? "not_leader" :
                    IsActive ? "war_active" :
                    !Occupied.TryGetValue(npcID, out var o) || o.ClanID != membership.Clan.ID ? "not_occupier" :
                    null;
            if (reason != null)
            {
                Reject(client, "give_up", territory, reason);
                return;
            }
            lock (Sync)
                Occupied.Remove(npcID);
            await ClanWarRepository.RemoveOccupationAsync(npcID);
            LogEvents.For(typeof(ClanWar), LogEvents.ClanWarAbandoned).Information(
                "{Name} gave up {Map} for clan {Clan} (clan {ClanId}, npc {NpcId})",
                client.Character.Name, territory.Name, membership.Clan.Name, membership.Clan.ID, npcID);
            await client.SendAsync(new MsgFamilyOccupy(OccupyAction.Abandoned, npcID) { MapName = territory.Name });
            await client.SendAsync(DialogFor(client, territory, DateTime.UtcNow));
            await Clans.RefreshInfoAsync(client);
        }

        // From the invitation, anywhere, or the banner's Join button
        private static async Task JoinAsync(Client client, uint npcID)
        {
            var territory = Find(npcID);
            var membership = Clans.Of(client.ID);
            var character = client.Character;
            War war = null;
            string reason;
            var now = DateTime.UtcNow;
            lock (Sync)
            {
                reason =
                    territory == null ? "unknown_map" :
                    !Wars.TryGetValue(npcID, out war) || war.Over ? "no_war" :
                    now >= StartedAt + EntryTime ? "entry_closed" :
                    membership == null || !war.Clans.Contains(membership.Clan.ID) ? "not_in_war" :
                    client.Life.Dead ? "dead" :
                    client.Map == war.Map ? "already_in" :
                    (Kernel.MapTypes.Of(character.MapID) & (MapFlags.PrisonMap | MapFlags.TeleportDisable)) != 0 ? "map" :
                    null;
                if (reason == null && !war.Returns.ContainsKey(client.ID))
                    war.Returns[client.ID] = new MapPoint(character.MapID, character.X, character.Y);
            }
            if (reason != null)
            {
                Reject(client, "join", territory, reason);
                return;
            }
            var spot = membership.Clan.ID == war.DefenderID ? DefenderSpot : AttackerSpot;
            if (!await Movement.TeleportAsync(client, war.Map, spot.X, spot.Y))
            {
                Reject(client, "join", territory, "bad_spot");
                return;
            }
            LogEvents.For(typeof(ClanWar), LogEvents.ClanWarEntered).Information(
                "{Name} of clan {Clan} entered the clan war of {Map} (clan {ClanId}, npc {NpcId})",
                character.Name, membership.Clan.Name, territory.Name, membership.Clan.ID, npcID);
        }

        private static async Task ClaimAsync(Client client, uint npcID)
        {
            var territory = Reach(client, npcID, "claim");
            if (territory == null)
                return;
            var membership = Clans.Of(client.ID);
            uint days;
            string reason;
            lock (Sync)
            {
                Occupied.TryGetValue(npcID, out var o);
                days = o?.Days ?? 0;
                reason =
                    membership?.Member.Rank != ClanRank.Leader || o == null || o.ClanID != membership.Clan.ID ? "not_occupier" :
                    IsActive ? "war_active" :
                    o.Claimed ? "claimed" :
                    client.Inventory == null || client.Inventory.FreeSlots == 0 ? "inventory_full" :
                    null;
                if (reason == null)
                    o.Claimed = true;
            }
            if (reason != null)
            {
                Reject(client, "claim", territory, reason);
                if (reason == "claimed")
                    await client.SendAsync(new MsgFamilyOccupy(OccupyAction.OnceADay, npcID));
                else if (reason == "inventory_full")
                    await client.SendAsync(new MsgFamilyOccupy(OccupyAction.NeedSpace, npcID));
                return;
            }

            uint pack = RevenueOf(territory, days);
            if (!Kernel.ItemTypes.TryGet(pack, out var type) ||
                await ItemActions.GiveAsync(client, type, "clan_war_revenue") == null)
            {
                lock (Sync)
                    if (Occupied.TryGetValue(npcID, out var o))
                        o.Claimed = false;
                Reject(client, "claim", territory, "no_item");
                return;
            }
            await SaveAsync(npcID);
            LogEvents.For(typeof(ClanWar), LogEvents.ClanWarRevenue).Information(
                "{Name} claimed the revenue of {Map} for clan {Clan} (pack {TypeId}, {Days} days)",
                client.Character.Name, territory.Name, membership.Clan.Name, pack, days);
            await client.SendAsync(new MsgFamilyOccupy(OccupyAction.RevenueClaimed, npcID) { PlayerID = client.ID });
            await client.SendAsync(DialogFor(client, territory, DateTime.UtcNow));
            await Clans.RefreshInfoAsync(client);
        }

        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try { await TickAsync(DateTime.UtcNow); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(ClanWar), LogEvents.ClanWarLoopFailed).Error(e, "Clan war loop failed");
                }
            }
        }

        public static async Task TickAsync(DateTime now)
        {
            bool scheduled = IsWarTime(Clock.Local(now));
            bool wantActive;
            List<War> settled = new List<War>();
            lock (Sync)
            {
                if (ManualOverride.HasValue && ManualOverride.Value == scheduled)
                    ManualOverride = null;
                wantActive = ManualOverride ?? scheduled;
                if (IsActive && now >= StartedAt + EntryTime)
                    foreach (var war in Wars.Values.Where(w => !w.Over))
                        if (war.Map.All().Where(p => !p.Life.Dead).Select(p => Clans.Of(p.ID)?.Clan.ID ?? 0)
                            .Where(war.Clans.Contains).Distinct().Count() <= 1)
                            settled.Add(war);
            }
            if (wantActive && !Active)
                await StartAsync(now, manual: false);
            else if (!wantActive && Active)
                await EndAsync(now, manual: false);
            else
                foreach (var war in settled)
                    await FinishAsync(war);
        }

        public static async Task<bool> StartAsync(DateTime now, bool manual = false)
        {
            var started = new List<War>();
            lock (Sync)
            {
                if (manual)
                    ManualOverride = true;
                if (IsActive)
                    return false;
                IsActive = true;
                StartedAt = now;
                Wars.Clear();
                bool haveMap = Kernel.Maps?.Exists(BattlefieldMapID) == true;
                foreach (var territory in Territories)
                {
                    var challengers = Signups.Where(s => s.Value == territory.NpcID).Select(s => s.Key).ToList();
                    if (challengers.Count == 0 || !haveMap)
                        continue;
                    var war = new War
                    {
                        Territory = territory,
                        Map = new GameMap<Client>(BattlefieldMapID, Kernel.Maps.Get(BattlefieldMapID)),
                        DefenderID = Occupied.TryGetValue(territory.NpcID, out var o) ? o.ClanID : 0,
                    };
                    war.Clans.UnionWith(challengers);
                    if (war.DefenderID != 0)
                        war.Clans.Add(war.DefenderID);
                    Wars[territory.NpcID] = war;
                    started.Add(war);
                }
            }

            await Chat.Talk.BroadcastAsync(started.Count > 0
                ? "The Clan War has begun! Clan members can enter the battlefield for 5 minutes."
                : "The Clan War has begun, but no clan signed up.");
            foreach (var war in started)
                foreach (var clanID in war.Clans)
                    foreach (var member in Clans.OnlineMembers(clanID))
                        await member.SendAsync(new MsgFamilyOccupy(OccupyAction.Invite, war.Territory.NpcID)
                        {
                            MapName = war.Territory.Name,
                        });
            LogEvents.For(typeof(ClanWar), LogEvents.ClanWarStarted).Information(
                "Clan war started ({Trigger}, {WarCount} maps: {Maps})", manual ? "manual" : "schedule",
                started.Count, string.Join(", ", started.Select(w => w.Territory.Name)));
            return true;
        }

        public static async Task<bool> EndAsync(DateTime now, bool manual = false)
        {
            List<War> open;
            lock (Sync)
            {
                if (manual)
                    ManualOverride = false;
                if (!IsActive)
                    return false;
                open = Wars.Values.Where(w => !w.Over).ToList();
            }
            foreach (var war in open)
                await FinishAsync(war);

            // Every other occupied map counts a day too, and today's revenue can be claimed
            List<uint> changed;
            lock (Sync)
            {
                IsActive = false;
                foreach (var (npcID, occupation) in Occupied)
                {
                    if (!Wars.ContainsKey(npcID))
                        occupation.Days++;
                    occupation.Claimed = false;
                }
                changed = Occupied.Keys.ToList();
                Wars.Clear();
            }
            foreach (var npcID in changed)
                await SaveAsync(npcID);
            await Chat.Talk.BroadcastAsync("The Clan War is over.");
            return true;
        }

        public static Result Decide(uint defenderID, IReadOnlyDictionary<uint, int> kills,
            IReadOnlyDictionary<uint, int> present, IEnumerable<uint> clans, Func<uint, bool> holdsAnother)
        {
            var ranked = clans
                .Select(id => (ID: id, Kills: kills.GetValueOrDefault(id), Present: present.GetValueOrDefault(id)))
                .OrderByDescending(x => x.Kills).ThenByDescending(x => x.Present)
                .ThenByDescending(x => x.ID == defenderID)
                .ToList();
            var top = ranked.FirstOrDefault();
            uint winner =
                top.ID == 0 || (top.Kills == 0 && top.Present == 0) ? defenderID :
                ranked.Any(x => x.ID == defenderID && x.Kills == top.Kills && x.Present == top.Present) ? defenderID :
                top.ID;
            bool taken = winner != 0 && winner != defenderID && !holdsAnother(winner);
            return new Result(0, taken ? winner : defenderID, defenderID, taken);
        }

        private static async Task FinishAsync(War war)
        {
            List<Client> players;
            Result result;
            lock (Sync)
            {
                if (war.Over)
                    return;
                war.Over = true;
                players = war.Map.All();
                var present = players.Where(p => !p.Life.Dead)
                    .GroupBy(p => Clans.Of(p.ID)?.Clan.ID ?? 0)
                    .ToDictionary(g => g.Key, g => g.Count());
                uint npcID = war.Territory.NpcID;
                result = Decide(war.DefenderID, war.Kills, present, war.Clans,
                    id => Occupied.Any(o => o.Key != npcID && o.Value.ClanID == id)) with { NpcID = npcID };
                if (result.Taken)
                    Occupied[npcID] = new Occupation { ClanID = result.WinnerID, Days = 1 };
                else if (Occupied.TryGetValue(npcID, out var held))
                    held.Days++;
                foreach (var clanID in Signups.Where(s => s.Value == npcID).Select(s => s.Key).ToList())
                    Signups.Remove(clanID);
            }

            await ClanWarRepository.RemoveSignupsForAsync(new[] { war.Territory.NpcID });
            await SaveAsync(war.Territory.NpcID);
            foreach (var player in players)
                Kernel.Post(player, () => LeaveAsync(player, war));

            var winner = Clans.Find(result.WinnerID);
            await Chat.Talk.BroadcastAsync(winner != null
                ? $"{winner.Name} has won the Clan War of {war.Territory.Name}!"
                : $"The Clan War of {war.Territory.Name} has ended without a victor.");
            foreach (var clanID in war.Clans)
                foreach (var member in Clans.OnlineMembers(clanID))
                    Kernel.Post(member, () => Clans.RefreshInfoAsync(member));
            LogEvents.For(typeof(ClanWar), LogEvents.ClanWarEnded).Information(
                "Clan war of {Map} ended (npc {NpcId}, winner {WinnerId} {WinnerName}, taken {Taken}, kills {Kills})",
                war.Territory.Name, war.Territory.NpcID, result.WinnerID, winner?.Name ?? "none", result.Taken,
                string.Join(", ", war.Kills.Select(k => $"{k.Key}:{k.Value}")));
        }

        // On the player's queue once their war is over
        private static async Task LeaveAsync(Client client, War war)
        {
            if (client.Character == null || client.Map != war.Map)
                return;
            if (client.Life.Dead)
                await Combat.Death.ReviveHereAsync(client, "clan_war");
            MapPoint back;
            lock (Sync)
                back = war.Returns.GetValueOrDefault(client.ID);
            if (back == null || !await Movement.TeleportAsync(client, back.MapID, back.X, back.Y))
            {
                var banner = Kernel.Npcs.All.FirstOrDefault(n => n.NpcID == war.Territory.NpcID);
                if (banner == null || !await Movement.TeleportAsync(client, banner.MapID, banner.X, (ushort)(banner.Y + 2)))
                    await Movement.TeleportAsync(client, StartPosition.MapID, StartPosition.X, StartPosition.Y);
            }
        }

        // Combat asks before a hit on a player: null when the attacker isn't on a battlefield,
        // else whether the two fight in the same war for different clans
        public static bool? CanFight(Client attacker, Client target)
        {
            if (attacker.Map?.Id != BattlefieldMapID)
                return null;
            lock (Sync)
            {
                var war = Wars.Values.FirstOrDefault(w => w.Map == attacker.Map);
                if (war == null || war.Over || target.Map != attacker.Map)
                    return false;
                uint mine = Clans.Of(attacker.ID)?.Clan.ID ?? 0, theirs = Clans.Of(target.ID)?.Clan.ID ?? 0;
                return mine != theirs && war.Clans.Contains(mine) && war.Clans.Contains(theirs);
            }
        }

        // True if the kill was on a battlefield: then it's no PK
        public static Task<bool> OnKillAsync(Client killer, Client victim)
        {
            if (killer.Map?.Id != BattlefieldMapID)
                return Task.FromResult(false);
            uint clanID = Clans.Of(killer.ID)?.Clan.ID ?? 0;
            lock (Sync)
            {
                var war = Wars.Values.FirstOrDefault(w => w.Map == killer.Map);
                if (war == null || war.Over)
                    return Task.FromResult(true);
                war.Kills[clanID] = war.Kills.GetValueOrDefault(clanID) + 1;
                LogEvents.For(typeof(ClanWar), LogEvents.ClanWarKill).Information(
                    "{Name} killed {Victim} in the clan war of {Map} (clan {ClanId}, {Kills} kills)",
                    killer.Character.Name, victim.Character.Name, war.Territory.Name, clanID, war.Kills[clanID]);
            }
            return Task.FromResult(true);
        }

        public static async Task OnClanDisbandedAsync(uint clanID)
        {
            bool changed;
            lock (Sync)
            {
                changed = Signups.Remove(clanID);
                foreach (var npcID in Occupied.Where(o => o.Value.ClanID == clanID).Select(o => o.Key).ToList())
                {
                    Occupied.Remove(npcID);
                    changed = true;
                }
            }
            if (changed)
                await ClanWarRepository.RemoveClanAsync(clanID);
        }

        public static string StatusText()
        {
            lock (Sync)
                return $"Clan War active={IsActive}, wars={Wars.Count}, occupied=" +
                    string.Join(", ", Occupied.Select(o => $"{Find(o.Key)?.Name}:{Clans.Find(o.Value.ClanID)?.Name}({o.Value.Days}d)")) +
                    $", sign-ups={Signups.Count}, schedule {StartHour:00}:{StartMinute:00}-{EndHour:00}:{EndMinute:00}.";
        }

        private static async Task SaveAsync(uint npcID)
        {
            DbFamilyOccupy row = null;
            lock (Sync)
                if (Occupied.TryGetValue(npcID, out var o))
                    row = new DbFamilyOccupy { NpcID = npcID, FamilyID = o.ClanID, Days = o.Days, RevenueClaimed = o.Claimed };
            if (row != null)
                await ClanWarRepository.SaveOccupationAsync(row);
            else
                await ClanWarRepository.RemoveOccupationAsync(npcID);
        }

        private static Territory Reach(Client client, uint npcID, string use)
        {
            var territory = Find(npcID);
            if (territory == null)
            {
                Reject(client, use, null, "unknown_map");
                return null;
            }
            return Dialogs.Reach(client, npcID, "clan_war_" + use) == null ? null : territory;
        }

        private static void Reject(Client client, string kind, Territory territory, string reason) =>
            LogEvents.For(typeof(ClanWar), LogEvents.ClanWarRejected).Warning(
                "{Name} can't {Kind} the clan war of {Map}: {Reason}",
                client.Character?.Name, kind, territory?.Name, reason);
    }
}
