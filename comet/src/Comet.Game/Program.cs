namespace Comet.Game
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
            Chat.Broadcasts.Interval = TimeSpan.FromSeconds(Math.Max(1, config.Broadcast.IntervalSeconds));
            _ = Chat.Broadcasts.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

            var saveInterval = TimeSpan.FromSeconds(Math.Max(1, config.Saving.IntervalSeconds));
            _ = SaveLoop.RunAsync(saveInterval, CancellationToken.None);

            await rpcServerTask;
            await serverTask;
        }
    }
}
