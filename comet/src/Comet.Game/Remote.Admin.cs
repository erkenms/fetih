namespace Comet.Game
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Admin;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.Pk;
    using Comet.Game.States;
    using Comet.Network.Sockets;
    using Comet.Shared.Models.Admin;

    // Admin calls for the web. The RPC port listens on localhost only, so whoever reaches it
    // runs on this machine; the web checks the caller's permissions before calling.
    public partial class Remote : IGameAdmin
    {
        private const string Reason = "admin";
        private const string MessageSender = "[GM]";
        private const byte MaxPlus = 12, MaxBless = 7;

        public Task<ServerState> ServerStatus()
        {
            var started = Process.GetCurrentProcess().StartTime.ToUniversalTime();
            var monsters = World.Monsters.All;
            return Task.FromResult(new ServerState
            {
                StartedAt = started,
                UptimeSeconds = Math.Round((DateTime.UtcNow - started).TotalSeconds),
                Connections = Kernel.Clients.Count,
                Players = Kernel.Players.Count,
                MapsLoaded = Kernel.World?.Loaded.Count() ?? 0,
                Monsters = monsters.Count,
                MonstersAlive = monsters.Count(m => m.Alive),
                GroundItems = Kernel.Ground?.Loaded.Sum(m => m.Count) ?? 0,
                Booths = Kernel.Players.Values.Count(p => p.Booth != null),
                BroadcastQueue = Broadcasts.Count,
                MonsterTickMilliseconds = World.MonsterAI.LastTick.TotalMilliseconds,
                SlowestMonsterTickMilliseconds = World.MonsterAI.SlowestTick.TotalMilliseconds,
                WorkingSetBytes = Environment.WorkingSet,
            });
        }

        public Task<List<OnlinePlayer>> OnlinePlayers() => Task.FromResult(
            Kernel.Players.Values
                .Where(p => p.Character != null)
                .Select(p => new OnlinePlayer
                {
                    CharacterID = p.Character.CharacterID,
                    AccountID = p.Character.AccountID,
                    Name = p.Character.Name,
                    Level = p.Character.Level,
                    Class = p.Character.CurrentClass,
                    Rebirths = p.Character.Rebirths,
                    MapID = p.Character.MapID,
                    X = p.Character.X,
                    Y = p.Character.Y,
                    PkPoints = p.Character.KillPoints,
                    Authority = (ushort)p.Authority,
                    IPAddress = p.IPAddress,
                    LoggedInAt = p.LoggedInAt,
                })
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());

        public Task<MapState> MapSnapshot(uint mapID) => Task.FromResult(Snapshot(mapID));

        private static MapState Snapshot(uint mapID)
        {
            if (!Kernel.Maps.Exists(mapID) || !Kernel.World.TryGet(mapID, out var map))
                return null;
            var now = DateTime.UtcNow;
            var snapshot = new MapState
            {
                MapID = mapID,
                Width = map.Grid.Width,
                Height = map.Grid.Height,
                TakenAt = now,
            };

            foreach (var player in map.All())
            {
                var character = player.Character;
                if (character == null || !map.TryGetPosition(player, out var x, out var y))
                    continue;
                snapshot.Players.Add(new MapPlayer
                {
                    CharacterID = character.CharacterID,
                    Name = character.Name,
                    Level = character.Level,
                    Class = character.CurrentClass,
                    X = x,
                    Y = y,
                    PkPoints = character.KillPoints,
                    NameColor = NameColor(player, now),
                    Dead = player.Life.Dead,
                    Health = character.HealthPoints,
                    MaxHealth = MaxHealthOf(player),
                    Mesh = character.Mesh,
                });
                if (player.Booth is { } booth)
                    snapshot.Booths.Add(new MapBooth
                    {
                        ID = booth.ID,
                        OwnerID = character.CharacterID,
                        OwnerName = character.Name,
                        X = booth.X,
                        Y = booth.Y,
                        Items = booth.Items.Count,
                        Hawk = booth.Hawk?.Message,
                    });
            }

            if (Kernel.Monsters != null && Kernel.Monsters.TryGet(mapID, out var monsters))
                foreach (var monster in monsters.All())
                    snapshot.Monsters.Add(new MapMonster
                    {
                        ID = monster.Identity,
                        TypeID = monster.Type.ID,
                        Name = monster.Type.Name,
                        Level = monster.Type.Level,
                        Lookface = monster.Type.Lookface,
                        X = monster.X,
                        Y = monster.Y,
                        Alive = monster.Alive,
                        Health = monster.Health,
                        MaxHealth = monster.Type.Life,
                        Guard = monster.IsGuard,
                    });

            if (Kernel.Ground.TryGet(mapID, out var ground))
                foreach (var item in ground.All())
                    snapshot.GroundItems.Add(new MapGroundItem
                    {
                        ID = item.Identity,
                        TypeID = item.TypeID,
                        X = item.X,
                        Y = item.Y,
                        Silver = item.Silver,
                        ExpiresAt = item.ExpiresAt,
                    });

            foreach (var npc in Kernel.Npcs.On(mapID))
                snapshot.Npcs.Add(new MapNpc { ID = npc.NpcID, Lookface = npc.Lookface, Role = npc.Role, X = npc.X, Y = npc.Y });
            return snapshot;
        }

        private static string NameColor(Client player, DateTime now) =>
            player.Pk.IsFlashing(now) ? "blue" :
            player.Character.KillPoints >= Pking.BlackNamePoints ? "black" :
            player.Character.KillPoints >= Pking.RedNamePoints ? "red" : "white";

        // Read off the player's queue, so equipment may be changing under it
        private static uint MaxHealthOf(Client player)
        {
            try { return Attributes.MaxHealth(player); }
            catch (InvalidOperationException) { return player.Character.HealthPoints; }
        }

        public async Task<CharacterState> Character(uint characterID)
        {
            if (Kernel.Players.TryGet(characterID, out var player) && player.Character != null)
            {
                var (ran, state) = await CharacterAccess.OnQueueAsync(player, () => Task.FromResult(LiveState(player)));
                if (ran)
                    return state;
            }

            var saved = await CharactersRepository.FindByIdAsync(characterID);
            if (saved == null)
                return null;
            var offline = StateOf(saved);
            offline.Items = (await ItemsRepository.LoadAsync(characterID)).Select(ItemOf).ToList();
            return offline;
        }

        private static CharacterState LiveState(Client player)
        {
            var state = StateOf(player.Character);
            state.Online = true;
            state.MaxHealth = Attributes.MaxHealth(player);
            state.MaxMana = Attributes.MaxMana(player);
            state.IPAddress = player.IPAddress;
            state.LoggedInAt = player.LoggedInAt;
            state.Dead = player.Life.Dead;
            if (player.Map != null && player.Map.TryGetPosition(player, out var x, out var y))
                (state.MapID, state.X, state.Y) = (player.Map.Id, x, y);
            var items = player.Equipment.Items.Concat(player.Inventory.Items);
            if (player.Warehouses != null)
                items = items.Concat(player.Warehouses.Items);
            state.Items = items.Select(ItemOf).ToList();
            return state;
        }

        private static CharacterState StateOf(DbCharacter c) => new CharacterState
        {
            CharacterID = c.CharacterID,
            AccountID = c.AccountID,
            Name = c.Name,
            Spouse = c.Spouse,
            Mesh = c.Mesh,
            Hairstyle = c.Hairstyle,
            Level = c.Level,
            Experience = c.Experience,
            Class = c.CurrentClass,
            PreviousClass = c.PreviousClass,
            AncestorClass = c.AncestorClass,
            Rebirths = c.Rebirths,
            Silver = c.Silver,
            StoredSilver = c.StoredSilver,
            ConquerPoints = c.Jewels,
            Strength = c.Strength,
            Agility = c.Agility,
            Vitality = c.Vitality,
            Spirit = c.Spirit,
            AttributePoints = c.AttributePoints,
            Health = c.HealthPoints,
            Mana = c.ManaPoints,
            PkPoints = c.KillPoints,
            Virtue = c.Virtue,
            MapID = c.MapID,
            X = c.X,
            Y = c.Y,
            MutedUntil = c.MutedUntil,
            Registered = c.Registered,
        };

        private static CharacterItem ItemOf(DbItem i) => new CharacterItem
        {
            ItemID = i.ItemID,
            TypeID = i.TypeID,
            Position = i.Position,
            Amount = i.Amount,
            AmountLimit = i.AmountLimit,
            Plus = i.Plus,
            Bless = i.Bless,
            Enchant = i.Enchant,
            Gem1 = i.Gem1,
            Gem2 = i.Gem2,
            Color = i.Color,
            Locked = i.Locked,
            Package = i.Package,
        };

        public async Task<AdminResult> Kick(KickArgs args)
        {
            AdminResult result;
            if (Kernel.Players.TryGet(args.CharacterID, out var player) && player.Character != null)
            {
                if (!string.IsNullOrWhiteSpace(args.Reason))
                    await Talk.NoticeAsync(player, "You have been kicked: " + args.Reason.Trim());
                player.Disconnect(DisconnectReasons.Kicked);
                result = AdminResult.Done(true);
            }
            else
            {
                result = AdminResult.Fail(AdminErrors.Offline);
            }
            return Logged(LogEvents.GmKick, args, args.CharacterID, args.Reason ?? "", result);
        }

        public async Task<AdminResult> Mute(MuteArgs args)
        {
            if (args.Minutes < 0 || args.Minutes > GmActions.MaxMuteMinutes)
                return Logged(LogEvents.GmMute, args, args.CharacterID, $"{args.Minutes} minutes",
                    AdminResult.Fail(AdminErrors.Invalid));
            var result = await OnCharacterAsync(args.CharacterID,
                async client =>
                {
                    if (args.Minutes > 0)
                        await GmActions.MuteAsync(client, args.Minutes);
                    else
                        await GmActions.UnmuteAsync(client);
                    return null;
                },
                async saved =>
                {
                    saved.MutedUntil = args.Minutes > 0 ? DateTime.UtcNow.AddMinutes(args.Minutes) : null;
                    await CharactersRepository.SaveAsync(saved);
                    return null;
                });
            return Logged(LogEvents.GmMute, args, args.CharacterID, $"{args.Minutes} minutes", result);
        }

        public async Task<AdminResult> Jail(CharacterArgs args)
        {
            var result = await OnCharacterAsync(args.CharacterID,
                async client => await Pk.Jail.SendAsync(client, Reason) ? null : AdminErrors.BadDestination,
                async saved =>
                {
                    if (!Kernel.Maps.Exists(Pk.Jail.MapID))
                        return AdminErrors.BadDestination;
                    (saved.MapID, saved.X, saved.Y) = (Pk.Jail.MapID, Pk.Jail.X, Pk.Jail.Y);
                    await CharactersRepository.SaveAsync(saved);
                    return null;
                });
            return Logged(LogEvents.GmJail, args, args.CharacterID, "", result);
        }

        public async Task<AdminResult> Ban(BanArgs args)
        {
            bool online = Kernel.Clients.TryGet(args.AccountID, out _);
            var result = await GmActions.BanAsync(args.AccountID, args.Reason)
                ? AdminResult.Done(online) : AdminResult.Fail(AdminErrors.NotFound);
            LogEvents.For<Remote>(LogEvents.GmBan).Information(
                "Admin account {ActorAccountId} banned account {AccountId} ({Detail}): {Result}",
                args.ActorAccountID, args.AccountID, args.Reason ?? "", ResultText(result));
            return result;
        }

        public async Task<AdminResult> Unban(BanArgs args)
        {
            var result = await AccountsRepository.UnbanAsync(args.AccountID) switch
            {
                AccountsRepository.UnbanResult.Unbanned => AdminResult.Done(false),
                AccountsRepository.UnbanResult.NotBanned => AdminResult.Fail(AdminErrors.NotBanned),
                _ => AdminResult.Fail(AdminErrors.NotFound),
            };
            LogEvents.For<Remote>(LogEvents.GmUnban).Information(
                "Admin account {ActorAccountId} unbanned account {AccountId} ({Detail}): {Result}",
                args.ActorAccountID, args.AccountID, args.Reason ?? "", ResultText(result));
            return result;
        }

        public async Task<AdminResult> Teleport(TeleportArgs args)
        {
            string detail = $"map {args.MapID} at {args.X},{args.Y}";
            if (!Kernel.World.TryGet(args.MapID, out var map) || !map.Grid.IsWalkable(args.X, args.Y))
                return Logged(LogEvents.GmTeleport, args, args.CharacterID, detail, AdminResult.Fail(AdminErrors.BadDestination));
            var result = await OnCharacterAsync(args.CharacterID,
                async client => await World.Movement.TeleportAsync(client, map, args.X, args.Y) ? null : AdminErrors.BadDestination,
                async saved =>
                {
                    (saved.MapID, saved.X, saved.Y) = (args.MapID, args.X, args.Y);
                    await CharactersRepository.SaveAsync(saved);
                    return null;
                });
            return Logged(LogEvents.GmTeleport, args, args.CharacterID, detail, result);
        }

        public async Task<AdminResult> GiveItem(GiveItemArgs args)
        {
            string detail = $"{args.Count} of type {args.TypeID} +{args.Plus} -{args.Bless} gems {args.Gem1},{args.Gem2}";
            if (!Kernel.ItemTypes.TryGet(args.TypeID, out var type))
                return Logged(LogEvents.GmGiveItem, args, args.CharacterID, detail, AdminResult.Fail(AdminErrors.UnknownItemType));
            if (args.Count < 1 || args.Count > Inventory.Capacity || args.Plus > MaxPlus || args.Bless > MaxBless)
                return Logged(LogEvents.GmGiveItem, args, args.CharacterID, detail, AdminResult.Fail(AdminErrors.Invalid));

            void Shape(DbItem item)
            {
                item.Plus = args.Plus;
                item.Bless = args.Bless;
                item.Gem1 = args.Gem1 ?? item.Gem1;
                item.Gem2 = args.Gem2 ?? item.Gem2;
            }

            var result = await OnCharacterAsync(args.CharacterID,
                async client =>
                {
                    if (client.Inventory.FreeSlots < args.Count)
                        return AdminErrors.InventoryFull;
                    for (int i = 0; i < args.Count; i++)
                        if (await ItemActions.GiveAsync(client, type, Reason, Shape) == null)
                            return AdminErrors.InventoryFull;
                    return null;
                },
                async saved =>
                {
                    var items = await ItemsRepository.LoadAsync(saved.CharacterID);
                    if (Inventory.Capacity - items.Count(i => i.Position == (byte)ItemPosition.Inventory) < args.Count)
                        return AdminErrors.InventoryFull;
                    for (int i = 0; i < args.Count; i++)
                    {
                        var item = ItemActions.New(saved.CharacterID, type);
                        Shape(item);
                        await ItemsRepository.CreateAsync(item);
                        LogEvents.For<Remote>(LogEvents.ItemCreated).Information(
                            "{Name} got {ItemName} (item {ItemId}, type {TypeId}; {Reason})",
                            saved.Name, type.Name, item.ItemID, type.ID, Reason);
                    }
                    return null;
                });
            return Logged(LogEvents.GmGiveItem, args, args.CharacterID, detail, result);
        }

        public async Task<AdminResult> TakeItem(TakeItemArgs args)
        {
            var result = await OnCharacterAsync(args.CharacterID,
                async client =>
                {
                    var item = client.Inventory.Find(args.ItemID);
                    if (item == null)
                        return AdminErrors.NotFound;
                    client.Inventory.Remove(item);
                    await ItemsRepository.DeleteAsync(item.ItemID);
                    await client.SendAsync(MsgItem.Removed(item.ItemID));
                    LogSpent(client.Character.Name, item);
                    return null;
                },
                async saved =>
                {
                    var item = (await ItemsRepository.LoadAsync(saved.CharacterID))
                        .FirstOrDefault(i => i.ItemID == args.ItemID && i.Position == (byte)ItemPosition.Inventory);
                    if (item == null)
                        return AdminErrors.NotFound;
                    await ItemsRepository.DeleteAsync(item.ItemID);
                    LogSpent(saved.Name, item);
                    return null;
                });
            return Logged(LogEvents.GmTakeItem, args, args.CharacterID, $"item {args.ItemID}", result);
        }

        private static void LogSpent(string name, DbItem item) =>
            LogEvents.For<Remote>(LogEvents.ItemSpent).Information(
                "{Name} spent item {ItemId} (type {TypeId}; {Reason})", name, item.ItemID, item.TypeID, Reason);

        public async Task<AdminResult> AddSilver(AmountArgs args)
        {
            var result = await OnCharacterAsync(args.CharacterID,
                async client => await Progress.ChangeSilverAsync(client, args.Amount, Reason) ? null : AdminErrors.OutOfRange,
                async saved =>
                {
                    long balance = saved.Silver + args.Amount;
                    if (balance < 0 || balance > uint.MaxValue)
                        return AdminErrors.OutOfRange;
                    saved.Silver = (uint)balance;
                    await CharactersRepository.SaveAsync(saved);
                    LogEvents.For<Remote>(LogEvents.CharacterSilver).Information(
                        "{Name} silver {Amount:+#;-#;0} to {Balance} ({Reason})", saved.Name, args.Amount, saved.Silver, Reason);
                    return null;
                });
            return Logged(LogEvents.GmSilver, args, args.CharacterID, args.Amount.ToString("+#;-#;0"), result);
        }

        public async Task<AdminResult> AddConquerPoints(AmountArgs args)
        {
            var result = await OnCharacterAsync(args.CharacterID,
                async client => await Progress.ChangeConquerPointsAsync(client, args.Amount, Reason) ? null : AdminErrors.OutOfRange,
                async saved =>
                {
                    long balance = saved.Jewels + args.Amount;
                    if (balance < 0 || balance > uint.MaxValue)
                        return AdminErrors.OutOfRange;
                    saved.Jewels = (uint)balance;
                    await CharactersRepository.SaveAsync(saved);
                    LogEvents.For<Remote>(LogEvents.CharacterConquerPoints).Information(
                        "{Name} conquer points {Amount:+#;-#;0} to {Balance} ({Reason})", saved.Name, args.Amount, saved.Jewels, Reason);
                    return null;
                });
            return Logged(LogEvents.GmConquerPoints, args, args.CharacterID, args.Amount.ToString("+#;-#;0"), result);
        }

        public async Task<AdminResult> SetLevel(LevelArgs args)
        {
            if (args.Level < 1 || args.Level > Kernel.Experience.MaxLevel)
                return Logged(LogEvents.GmLevel, args, args.CharacterID, $"level {args.Level}", AdminResult.Fail(AdminErrors.OutOfRange));
            var result = await OnCharacterAsync(args.CharacterID,
                async client => await Progress.SetLevelAsync(client, args.Level, Reason) ? null : AdminErrors.OutOfRange,
                async saved =>
                {
                    Progress.SetLevel(saved, args.Level);
                    await CharactersRepository.SaveAsync(saved);
                    return null;
                });
            return Logged(LogEvents.GmLevel, args, args.CharacterID, $"level {args.Level}", result);
        }

        // A whisper from MessageSender; only to a player online
        public async Task<AdminResult> Message(MessageArgs args)
        {
            AdminResult result;
            if (string.IsNullOrWhiteSpace(args.Text))
                result = AdminResult.Fail(AdminErrors.Invalid);
            else if (!Kernel.Players.TryGet(args.CharacterID, out var player) || player.Character == null)
                result = AdminResult.Fail(AdminErrors.Offline);
            else
            {
                await player.SendAsync(new MsgTalk(0, MsgTalk.TalkChannel.Whisper, System.Drawing.Color.White,
                    player.Character.Name, MessageSender, args.Text.Trim()));
                result = AdminResult.Done(true);
            }
            return Logged(LogEvents.GmMessage, args, args.CharacterID, args.Text ?? "", result);
        }

        public async Task<AdminResult> Broadcast(BroadcastArgs args)
        {
            var channel = args.Channel switch
            {
                BroadcastChannel.System => MsgTalk.TalkChannel.System,
                BroadcastChannel.TopLeft => MsgTalk.TalkChannel.TopLeft,
                _ => MsgTalk.TalkChannel.Center,
            };
            AdminResult result = AdminResult.Fail(AdminErrors.Invalid);
            if (!string.IsNullOrWhiteSpace(args.Text))
            {
                await GmActions.BroadcastAsync(channel, args.Text.Trim());
                result = AdminResult.Done(true);
            }
            LogEvents.For<Remote>(LogEvents.GmBroadcast).Information(
                "Admin account {ActorAccountId} broadcast on {Channel} ({Detail}): {Result}",
                args.ActorAccountID, channel.ToString(), args.Text ?? "", ResultText(result));
            return result;
        }

        // The handlers return null when done, or an AdminErrors code
        private static async Task<AdminResult> OnCharacterAsync(uint characterID,
            Func<Client, Task<string>> online, Func<DbCharacter, Task<string>> offline)
        {
            var (outcome, isOnline, error) = await CharacterAccess.RunAsync(characterID, online, offline);
            return outcome switch
            {
                CharacterAccess.Outcome.NotFound => AdminResult.Fail(AdminErrors.NotFound),
                CharacterAccess.Outcome.Busy => AdminResult.Fail(AdminErrors.Busy),
                _ when error != null => AdminResult.Fail(error, isOnline),
                _ => AdminResult.Done(isOnline),
            };
        }

        private static AdminResult Logged(string eventName, AdminArgs args, uint characterID, string detail, AdminResult result)
        {
            LogEvents.For<Remote>(eventName).Information(
                "Admin account {ActorAccountId} ran {Action} on character {CharacterId} ({Detail}): {Result}",
                args.ActorAccountID, eventName.Substring("gm.".Length), characterID, detail, ResultText(result));
            return result;
        }

        private static string ResultText(AdminResult result) =>
            result.Ok ? (result.Online ? "online" : "offline") : result.Error;
    }
}
