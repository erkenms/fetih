namespace Comet.Game
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Network.Sockets;
    using Comet.Game.Database;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Npcs;
    using Comet.Game.Packets;
    using Comet.Game.Pk;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.RPC;
    using Comet.Network.Security;
    using Serilog;

    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            Console.Title = "Comet, Game Server";
            Console.WriteLine();
            Console.WriteLine("  Comet: Game Server");
            Console.WriteLine("  Spirited (C) All Rights Reserved");
            Console.WriteLine("  Patch 5619");
            Console.WriteLine();

            var config = new ServerConfiguration(args);
            LogSetup.Configure("game", config.Logging);

            // podman stop sends SIGTERM, Ctrl+C SIGINT; either logs everyone out with a save
            var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
            using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
            void Stop(PosixSignalContext context)
            {
                context.Cancel = true;
                shutdown.TrySetResult();
            }

            PacketTrace.Configure(config.Logging?.PacketTrace);
            if (!config.Valid) 
            {
                Log.Error("Invalid server configuration file");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Clock.Zone = string.IsNullOrEmpty(config.TimeZone)
                    ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(config.TimeZone);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException || e is InvalidTimeZoneException)
            {
                Log.Error("Unknown time zone {TimeZone} in the configuration", config.TimeZone);
                Log.CloseAndFlush();
                return;
            }
            Log.Information("Initializing server in time zone {TimeZone}...", Clock.Zone.Id);
            DataFiles.Directory = config.Data.ResolveDirectory();
            MsgConnect.StrictAuthentication = config.Authentication.StrictAuthPass;
            ServerDbContext.Configuration = config.Database;
            AccountsRepository.Configuration = config.AccountDatabase;
            if (!ServerDbContext.Ping())
            {
                Log.Error("Invalid database configuration");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.Maps = new MapStore(config.Maps.ResolveClientDirectory());
                Kernel.World = new WorldMaps<Client>(Kernel.Maps);
                Kernel.Ground = new WorldMaps<GroundItem>(Kernel.Maps);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read map data from {Directory}; set Maps:ClientDirectory " +
                    "to the client built by client/build.cs", config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.HonorRewards = Arena.HonorRewards.Load(config.Maps.ResolveClientDirectory());
                LogEvents.For(typeof(Program), LogEvents.HonorRewardsLoaded).Information(
                    "Loaded honor rewards for {RankCount} qualifier ranks", Kernel.HonorRewards.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read ini/HonorRewards.ini from {Directory}", config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Events.EventScheduler.Schedule = Events.EventSchedule.Load(config.Maps.ResolveClientDirectory())
                    .With(Tournaments.GuildPk.Events);
                LogEvents.For(typeof(Program), LogEvents.EventsLoaded).Information(
                    "Loaded {EventCount} weekly events from client ini", Events.EventScheduler.Schedule.All.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read ini/Activity.ini from {Directory}", config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Social.CaptureTheFlag.Layout = Social.WarFlagArena.Load(config.Maps.ResolveClientDirectory(), Social.CaptureTheFlag.MapID);
                LogEvents.For(typeof(Program), LogEvents.CaptureTheFlagLoaded).Information(
                    "Loaded the Capture the Flag arena: {BaseCount} flag bases, {PrizeCount} prizes",
                    Social.CaptureTheFlag.Layout.Bases.Count, Social.CaptureTheFlag.Layout.Prizes.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read the Capture the Flag arena (ini/region.ini, info.ini) from {Directory}",
                    config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.TutorTypes = Social.TutorTypes.Load(config.Maps.ResolveClientDirectory());
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read ini/tutorType.ini from {Directory}", config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.SubClasses = SubClassTable.Load(config.Maps.ResolveClientDirectory());
                LogEvents.For(typeof(Program), LogEvents.SubClassesLoaded).Information(
                    "Loaded {SubClassCount} sub-classes from client ini", Kernel.SubClasses.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read ini/SubProfessionInfo.ini or ini/levexp.dat from {Directory}",
                    config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.Refineries = RefineryTable.Load(config.Maps.ResolveClientDirectory());
                LogEvents.For(typeof(Program), LogEvents.RefineryLoaded).Information(
                    "Loaded {RefineryCount} refinery items from client ini", Kernel.Refineries.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read the refinery files (ini/ItemQuench.ini, ItemQuenchTipInfo.ini, " +
                    "ItemSolidify.ini, info.ini) from {Directory}", config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.ArsenalConfig = Social.ArsenalConfig.Load(config.Maps.ResolveClientDirectory());
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read ini/totempole.ini from {Directory}", config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.ItemTypes = ItemTypeTable.Load(config.Maps.ResolveClientDirectory());
                Kernel.ItemAdditions = ItemAdditionTable.Load(config.Maps.ResolveClientDirectory());
                LogEvents.For(typeof(Program), LogEvents.ItemTypesLoaded).Information(
                    "Loaded {ItemTypeCount} item types ({SkippedCount} records skipped) and " +
                    "{ItemAdditionCount} item additions",
                    Kernel.ItemTypes.Count, Kernel.ItemTypes.Skipped, Kernel.ItemAdditions.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read ini/itemtype.dat or ini/itemadd.ini from {Directory}",
                    config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.Magics = Magic.MagicTypeTable.Load(config.Maps.ResolveClientDirectory());
                Kernel.Proficiencies = Magic.ProficiencyTable.Load(config.Maps.ResolveClientDirectory());
                Kernel.MagicOps = Magic.MagicTypeOpTable.Load(config.Maps.ResolveClientDirectory());
                Kernel.MagicEffects = Magic.MagicEffectTable.Load(config.Maps.ResolveClientDirectory());
                LogEvents.For(typeof(Program), LogEvents.MagicTypesLoaded).Information(
                    "Loaded {MagicTypeCount} spell levels ({SkippedCount} records skipped), " +
                    "{ProficiencyLevelCount} proficiency levels, {MagicOpCount} rebirth spell operations and {MagicEffectCount} spell shows",
                    Kernel.Magics.Count, Kernel.Magics.Skipped, Kernel.Proficiencies.MaxLevel, Kernel.MagicOps.Count,
                    Kernel.MagicEffects.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read ini/magictype.dat, ini/WeaponSkillLevelExp.ini or ini/magictypeop.dat from {Directory}",
                    config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.Portals = new PortalTable(PortalTable.Load(DataFiles.PathOf("portals.json")));
                LogEvents.For(typeof(Program), LogEvents.PortalsLoaded).Information(
                    "Loaded {PortalCount} portals", Kernel.Portals.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read Data/portals.json");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.MapTypes = new MapTypes(MapTypes.Load(DataFiles.PathOf("maps.json")));
                LogEvents.For(typeof(Program), LogEvents.MapTypesLoaded).Information(
                    "Loaded type flags for {MapCount} maps", Kernel.MapTypes.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read Data/maps.json");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                var npcs = NpcTable.Load(DataFiles.PathOf("npcs.json"), DataFiles.PathOf("goals.json"));
                var placed = npcs.Where(n => Kernel.Maps.Exists(n.MapID)).ToList();
                Kernel.Npcs = new NpcTable(placed);
                LogEvents.For(typeof(Program), LogEvents.NpcsLoaded).Information(
                    "Loaded {NpcCount} NPCs on {MapCount} maps ({SkippedCount} on maps the client doesn't have)",
                    Kernel.Npcs.Count, Kernel.Npcs.MapIDs.Count(), npcs.Count - placed.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read Data/npcs.json or Data/goals.json");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                var teleporters = Teleporters.Load(DataFiles.PathOf("teleporters.json"));
                var nowhere = teleporters.SelectMany(t => t.Destinations.Select(d => (t.NpcID, d)))
                    .Where(x => !Kernel.Maps.Exists(x.d.MapID)).ToList();
                foreach (var (npcID, destination) in nowhere)
                    Log.Warning("Teleporter {NpcId} leads to map {MapId}, which the client doesn't have",
                        npcID, destination.MapID);
                Kernel.Teleporters = new Teleporters(teleporters);
                LogEvents.For(typeof(Program), LogEvents.TeleportersLoaded).Information(
                    "Loaded {TeleporterCount} teleporters", Kernel.Teleporters.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read Data/teleporters.json");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.Talkers = new Talkers(Talkers.Load(DataFiles.PathOf("talkers.json")));
                LogEvents.For(typeof(Program), LogEvents.TalkersLoaded).Information(
                    "Loaded {TalkerCount} NPCs that only talk", Kernel.Talkers.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read Data/talkers.json");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                await Social.Guilds.LoadAsync();
                LogEvents.For(typeof(Program), LogEvents.GuildsLoaded).Information(
                    "Loaded {GuildCount} guilds", Social.Guilds.Count);
                Social.GuildWar.StartDay = config.GuildWar.StartDay;
                Social.GuildWar.StartHour = config.GuildWar.StartHour;
                Social.GuildWar.StartMinute = config.GuildWar.StartMinute;
                Social.GuildWar.EndDay = config.GuildWar.EndDay;
                Social.GuildWar.EndHour = config.GuildWar.EndHour;
                Social.GuildWar.EndMinute = config.GuildWar.EndMinute;
                Social.GuildWar.PoleMaxLife = Math.Max(1u, config.GuildWar.PoleLife);
                Social.GuildWar.GateMaxLife = Math.Max(1u, config.GuildWar.GateLife);
                Social.GuildWar.PrizeConquerPoints = config.GuildWar.PrizeConquerPoints;
                await Social.GuildWar.LoadAsync();
                await Social.Clans.LoadAsync();
                LogEvents.For(typeof(Program), LogEvents.ClansLoaded).Information(
                    "Loaded {ClanCount} clans", Social.Clans.Count);
                Social.ClanWar.StartHour = config.ClanWar.StartHour;
                Social.ClanWar.StartMinute = config.ClanWar.StartMinute;
                Social.ClanWar.EndHour = config.ClanWar.EndHour;
                Social.ClanWar.EndMinute = config.ClanWar.EndMinute;
                Social.ClanWar.SignUpCloseHour = config.ClanWar.SignUpCloseHour;
                Social.ClanWar.SignUpCloseMinute = config.ClanWar.SignUpCloseMinute;
                Social.ClanWar.EntryTime = TimeSpan.FromMinutes(Math.Max(0, config.ClanWar.EntryMinutes));
                Social.ClanWar.Fee = config.ClanWar.Fee;
                await Social.ClanWar.LoadAsync();
                await Tournaments.ElitePk.LoadAsync();
                await Tournaments.TeamPk.LoadAllAsync();
                await Tournaments.GuildPk.LoadAsync();
                await Social.GuildRewards.LoadAsync();
                await Arena.Qualifier.LoadAsync();
                LogEvents.For(typeof(Program), LogEvents.ArenaLoaded).Information(
                    "Loaded {RecordCount} arena records", Arena.Qualifier.Count);
                await Nobility.Peerage.LoadAsync();
                LogEvents.For(typeof(Program), LogEvents.NobilityLoaded).Information(
                    "Loaded {DonorCount} nobility donors", Nobility.Peerage.Count);
                await Flowers.FlowerRanking.LoadAsync();
                LogEvents.For(typeof(Program), LogEvents.FlowersLoaded).Information(
                    "Loaded {RecordCount} flower records", Flowers.FlowerRanking.Count);
                await Chat.MessageBoard.LoadAsync();
                LogEvents.For(typeof(Program), LogEvents.MessageBoardLoaded).Information(
                    "Loaded {PostCount} message board posts", Chat.MessageBoard.Count);
                await Social.Mentors.LoadAsync();
                LogEvents.For(typeof(Program), LogEvents.MentorsLoaded).Information(
                    "Loaded {RelationCount} mentor relations", Social.Mentors.Count);
                await Chat.Broadcasts.LoadAsync();
                LogEvents.For(typeof(Program), LogEvents.BroadcastsLoaded).Information(
                    "Loaded {BroadcastCount} queued broadcasts", Chat.Broadcasts.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't load guilds, clans, arena records and nobility from the database");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.Shops = ShopTable.Load(config.Maps.ResolveClientDirectory());
                LogEvents.For(typeof(Program), LogEvents.ShopsLoaded).Information(
                    "Loaded {ShopCount} shops", Kernel.Shops.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read ini/Shop.dat from {Directory}", config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.MonsterTypes = new MonsterTypeTable(MonsterTypeTable.Load(DataFiles.PathOf("monsters.json")));
                var spawnsFile = System.IO.Path.GetFullPath(config.Monsters.SpawnsFile, DataFiles.PathOf(""));
                var generators = Generator.Load(spawnsFile, Kernel.MonsterTypes);
                Kernel.Monsters = new WorldMaps<Monster>(Kernel.Maps);
                var (placed, skipped) = Monsters.Spawn(generators, Random.Shared);
                LogEvents.For(typeof(Program), LogEvents.MonstersLoaded).Information(
                    "Loaded {MonsterTypeCount} monster types and {SpawnCount} spawns from {SpawnsFile}; " +
                    "placed {MonsterCount} monsters ({SkippedCount} spawns on maps the client doesn't have)",
                    Kernel.MonsterTypes.Count, generators.Count, spawnsFile, placed, skipped);

                var dropsFile = System.IO.Path.GetFullPath(config.Monsters.DropsFile, DataFiles.PathOf(""));
                var drops = Combat.DropTable.Load(dropsFile, Kernel.MonsterTypes, Kernel.ItemTypes);
                Kernel.Drops = new Combat.DropTable(drops);
                LogEvents.For(typeof(Program), LogEvents.DropsLoaded).Information(
                    "Loaded per-monster drop tables for {DropTableCount} monsters from {DropsFile}",
                    Kernel.Drops.Count, dropsFile);

                var bossesFile = System.IO.Path.GetFullPath(config.Monsters.BossesFile, DataFiles.PathOf(""));
                var bossSpawns = BossSpawn.Load(bossesFile, Kernel.MonsterTypes);
                DataReload.SpawnsFile = spawnsFile;
                DataReload.DropsFile = dropsFile;
                DataReload.BossesFile = bossesFile;
                Kernel.Bosses = new BossSchedule();
                var (bossesRegistered, bossesSkipped) = Kernel.Bosses.Register(bossSpawns, DateTime.UtcNow);
                LogEvents.For(typeof(Program), LogEvents.BossesLoaded).Information(
                    "Loaded {BossCount} scheduled bosses from {BossesFile} ({SkippedCount} skipped)",
                    bossesRegistered, bossesFile, bossesSkipped);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read Data/monsters.json, spawns, drops or bosses files");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.Experience = new ExperienceTable(ExperienceTable.Load(DataFiles.PathOf("levels.json")));
                Log.Information("Loaded experience for {LevelCount} levels; the level cap is {MaxLevel}",
                    Kernel.Experience.Count, Kernel.Experience.MaxLevel);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read Data/levels.json");
                Log.CloseAndFlush();
                return;
            }

            var tasks = new List<Task>();
            tasks.Add(Kernel.Services.Randomness.StartAsync(CancellationToken.None));
            tasks.Add(DiffieHellman.ProbablePrimes.StartAsync(CancellationToken.None));
            await Task.WhenAll(tasks.ToArray());
            
            Log.Information("Launching server listeners...");
            var rpcServer = new RpcServerListener(new Remote());
            var rpcServerTask = rpcServer.StartAsync(config.RpcNetwork.Port, config.RpcNetwork.IPAddress);

            var server = new Server(config);
            var serverTask = server.StartAsync(config.GameNetwork.Port, config.GameNetwork.IPAddress);

            LogEvents.For(typeof(Program), LogEvents.ServerStart).Information(
                "Listening for new connections on {IPAddress}:{Port}",
                config.GameNetwork.IPAddress, config.GameNetwork.Port);
            
            Ground.Lifetime = TimeSpan.FromSeconds(Math.Max(1, config.Ground.ExpireSeconds));
            Combat.Death.ReviveDelay = TimeSpan.FromSeconds(Math.Max(0, config.Death.ReviveSeconds));
            Training.GodExp.OnlineInterval = TimeSpan.FromSeconds(Math.Max(1, config.Training.OnlineSeconds));
            Combat.Attacks.PlayerInterval = TimeSpan.FromMilliseconds(Math.Max(0, config.Combat.PlayerAttackMilliseconds));
            World.MonsterAI.WanderChance = Math.Clamp(config.Monsters.WanderChance, 0, 100);
            World.MonsterAI.Configure(config.Monsters);
            World.MonsterAI.AttackInterval = TimeSpan.FromMilliseconds(Math.Max(0, config.Combat.MonsterAttackMilliseconds));
            int lost = await ItemsRepository.DeleteGroundAsync((byte)ItemPosition.Ground);
            if (lost > 0)
                Log.Information("Deleted {Count} items left on the ground by the last run", lost);
            _ = Ground.RunExpiryAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            _ = MonsterAI.RunAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
            Pking.FlashTime = TimeSpan.FromSeconds(Math.Max(1, config.Pk.FlashSeconds));
            Pking.DecayInterval = TimeSpan.FromSeconds(Math.Max(1, config.Pk.DecaySeconds));
            _ = Pking.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            Magic.Recovery.XpInterval = TimeSpan.FromMilliseconds(Math.Max(100, config.Magic.XpMilliseconds));
            Magic.Recovery.XpFullTime = TimeSpan.FromSeconds(Math.Max(1, config.Magic.XpFullSeconds));
            Magic.Casting.MinimumDelay = TimeSpan.FromMilliseconds(Math.Max(0, config.Magic.MinimumCastMilliseconds));
            _ = Magic.Recovery.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            Arena.Qualifier.InviteTime = TimeSpan.FromSeconds(Math.Max(1, config.Arena.InviteSeconds));
            Arena.Qualifier.CountdownTime = TimeSpan.FromSeconds(Math.Max(0, config.Arena.CountdownSeconds));
            Arena.Qualifier.MatchTime = TimeSpan.FromSeconds(Math.Max(1, config.Arena.MatchSeconds));
            Arena.Qualifier.EndTime = TimeSpan.FromSeconds(Math.Max(0, config.Arena.EndSeconds));
            _ = Arena.Qualifier.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            _ = Social.GuildWar.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            _ = Social.ClanWar.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            Tournaments.ElitePk.KnockoutTime = TimeSpan.FromSeconds(Math.Max(1, config.ElitePk.KnockoutSeconds));
            Tournaments.ElitePk.FinalTime = TimeSpan.FromSeconds(Math.Max(1, config.ElitePk.FinalSeconds));
            Tournaments.ElitePk.CountdownTime = TimeSpan.FromSeconds(Math.Max(0, config.ElitePk.CountdownSeconds));
            Tournaments.ElitePk.RoundBreak = TimeSpan.FromSeconds(Math.Max(0, config.ElitePk.RoundBreakSeconds));
            Tournaments.ElitePk.LeaveDelay = TimeSpan.FromSeconds(Math.Max(0, config.ElitePk.LeaveSeconds));
            Tournaments.ElitePk.Subscribe();
            Social.CaptureTheFlag.FlagTime = TimeSpan.FromSeconds(Math.Max(1, config.CaptureTheFlag.FlagSeconds));
            Social.CaptureTheFlag.FlagCount = Math.Max(1, config.CaptureTheFlag.Flags);
            Social.CaptureTheFlag.DoubleInterval = TimeSpan.FromSeconds(Math.Max(1, config.CaptureTheFlag.DoubleSeconds));
            Social.CaptureTheFlag.Subscribe();
            _ = Social.CaptureTheFlag.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            _ = Tournaments.ElitePk.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            Tournaments.TeamPk.KnockoutTime = TimeSpan.FromSeconds(Math.Max(1, config.TeamPk.KnockoutSeconds));
            Tournaments.TeamPk.FinalTime = TimeSpan.FromSeconds(Math.Max(1, config.TeamPk.FinalSeconds));
            Tournaments.TeamPk.CountdownTime = TimeSpan.FromSeconds(Math.Max(0, config.TeamPk.CountdownSeconds));
            Tournaments.TeamPk.RoundBreak = TimeSpan.FromSeconds(Math.Max(0, config.TeamPk.RoundBreakSeconds));
            Tournaments.TeamPk.LeaveDelay = TimeSpan.FromSeconds(Math.Max(0, config.TeamPk.LeaveSeconds));
            Tournaments.TeamPk.SubscribeAll();
            _ = Tournaments.TeamPk.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            Tournaments.GuildPk.FlagLife = Math.Max(1u, config.GuildPk.FlagLife);
            Tournaments.GuildPk.MinGuildDays = Math.Max(0, config.GuildPk.MinGuildDays);
            Tournaments.GuildPk.ContestPrizes = config.GuildPk.ContestPrizeConquerPoints ?? Tournaments.GuildPk.ContestPrizes;
            Tournaments.GuildPk.TournamentPrizes = config.GuildPk.TournamentPrizeConquerPoints ?? Tournaments.GuildPk.TournamentPrizes;
            Tournaments.GuildPk.Subscribe();
            _ = Tournaments.GuildPk.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            _ = Events.EventScheduler.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            Chat.Broadcasts.Interval = TimeSpan.FromSeconds(Math.Max(1, config.Broadcast.IntervalSeconds));
            _ = Chat.Broadcasts.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

            GameMetrics.Start(config.Metrics);

            var saveInterval = TimeSpan.FromSeconds(Math.Max(1, config.Saving.IntervalSeconds));
            _ = SaveLoop.RunAsync(saveInterval, CancellationToken.None);

            var ended = await Task.WhenAny(shutdown.Task, rpcServerTask, serverTask);
            if (ended != shutdown.Task)
            {
                await ended;
                return;
            }

            await ShutdownAsync(server, TimeSpan.FromSeconds(Math.Max(1, config.Saving.ShutdownSeconds)));
        }

        private static async Task ShutdownAsync(Server server, TimeSpan timeout)
        {
            int online = Kernel.Players.Count;
            LogEvents.For(typeof(Program), LogEvents.ServerShutdown).Information(
                "Shutting down: logging out {Online} online characters", online);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            server.Stop();

            // Each disconnect registers its logout save in Kernel.Logouts before it leaves
            // Kernel.Clients, so both empty means every save has finished
            while ((Kernel.Clients.Count > 0 || !Kernel.Logouts.IsEmpty) && watch.Elapsed < timeout)
                await Task.Delay(100);

            if (Kernel.Clients.Count > 0 || !Kernel.Logouts.IsEmpty)
                LogEvents.For(typeof(Program), LogEvents.ServerShutdown).Error(
                    "Shutdown gave up after {DurationMs} ms with {Clients} clients still connected " +
                    "and {Saving} logouts still saving",
                    watch.ElapsedMilliseconds, Kernel.Clients.Count, Kernel.Logouts.Count);
            else
                LogEvents.For(typeof(Program), LogEvents.ServerShutdown).Information(
                    "Logged out {Online} characters in {DurationMs} ms", online, watch.ElapsedMilliseconds);
            Log.CloseAndFlush();
        }
    }
}
