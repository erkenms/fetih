namespace Comet.Game.Items
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.Pk;
    using Comet.Game.States;
    using Comet.Game.World;

    // Items used from the inventory (MsgItem 4 with slot 0): potions, city scrolls, praying stones,
    // EXP balls, EXP potions, PK amulets, hair dyes, fireworks, silver/CP packs, item packs,
    // skill books, and quivers (which go on the left hand). Runs on the
    // player's own packet queue, like Progress.
    public static class Using
    {
        // EXP balls: each level has an upLevTime, how long it is meant to take, and a ball is
        // worth 600 of it, carried through level-ups. So a ball is a fixed slice of levelling,
        // not a fixed amount of experience.
        public static readonly uint[] ExpBalls = { 723700, 723834, 723911 };
        public const byte ExpBallMaxLevel = 130;

        // Ten a day
        public const int ExpBallsPerDay = 10;

        public static int ExpBallsUsedOn(DbCharacter character, DateTime today) =>
            character.ExpBallDay == today.Date ? character.ExpBallsUsed : 0;

        public const uint MeteorScroll = 720027, DragonBallScroll = 720028;
        public const int ScrollGems = 10;

        public sealed record PackEntry(uint TypeID, int Count);
        public sealed record PkReducer(ushort Amount, ushort MinPoints, bool ForbidJail, TimeSpan Cooldown);

        // Fixed-content scrolls, 3-in-1 drug bundles, and gift packs from itemtype.dat
        private static readonly Dictionary<uint, PackEntry[]> ItemPacks = new Dictionary<uint, PackEntry[]>
        {
            [MeteorScroll] = new[] { new PackEntry(Upgrading.Meteor, ScrollGems) },
            [DragonBallScroll] = new[] { new PackEntry(Upgrading.DragonBall, ScrollGems) },
            // 3-in-1 drug bundles
            [720010] = new[] { new PackEntry(1000030, 3) }, // Amrita
            [720011] = new[] { new PackEntry(1002000, 3) }, // Panacea
            [720012] = new[] { new PackEntry(1002010, 3) }, // Ginseng
            [720013] = new[] { new PackEntry(1002020, 3) }, // Vanilla
            [720014] = new[] { new PackEntry(1001030, 3) }, // RecoveryPill
            [720015] = new[] { new PackEntry(1001040, 3) }, // SoulPill
            [720016] = new[] { new PackEntry(1002030, 3) }, // RefreshingPill
            [720017] = new[] { new PackEntry(1002040, 3) }, // ChantPill
            // Firework and fixed gift packs
            [720600] = new[] { new PackEntry(720601, 10) }, // LanternFestivalFireworks
            [721863] = new[] { new PackEntry(721862, 5) },  // FireworkPack
            [724002] = new[] { new PackEntry(711504, 3) },  // SmallLotteryTicketPack
            [721222] = new[] { new PackEntry(730008, 1), new PackEntry(730006, 1) }, // +8StonePack
            [721223] = new[] { new PackEntry(1200005, 1), new PackEntry(730003, 1) }, // ToughDrillPack
            [729210] = new[] { new PackEntry(723717, 1), new PackEntry(723714, 1), new PackEntry(723713, 1) }, // MoneyBagPack
        };

        // Money bags whose exact silver amount is stated in itemtype.dat
        private static readonly Dictionary<uint, uint> SilverBags = new Dictionary<uint, uint>
        {
            [723915] = 150_000,     // 150KMoneyBag
            [723713] = 300_000,     // Class1MoneyBag
            [728232] = 500_000,     // MoneyBag
            [723714] = 800_000,     // Class2MoneyBag
            [723715] = 1_200_000,   // Class3MoneyBag
            [723716] = 1_800_000,   // Class4MoneyBag
            [723717] = 5_000_000,   // Class5MoneyBag
            [723718] = 20_000_000,  // Class6MoneyBag
            [723719] = 25_000_000,  // Class7MoneyBag
            [723720] = 80_000_000,  // Class8MoneyBag
            [723721] = 100_000_000, // Class9MoneyBag
            [723722] = 300_000_000, // Class10MoneyBag
            [723723] = 500_000_000, // TopMoneyBag
        };

        // CP bags, packs, and lottery prize packs whose exact CP amount is stated in itemtype.dat
        private static readonly Dictionary<uint, uint> ConquerPointPacks = new Dictionary<uint, uint>
        {
            [729911] = 3,       // CPBag
            [720657] = 5,       // HopeCPPack
            [729912] = 6,       // CPBackpack
            [720656] = 10,      // MascotCPPack
            [720655] = 20,      // MammonCPPack
            [720658] = 25,      // FestivalCPPack
            [729244] = 45,      // 7thPrize
            [720659] = 50,      // SweetCPPack
            [720663] = 50,      // VioletCPPack
            [720660] = 100,     // LegendCPPack
            [720664] = 100,     // FlareCPPack
            [720665] = 200,     // CuteCPPack
            [720675] = 250,     // GhostCPPack
            [720653] = 270,     // DreamCPPack
            [729245] = 450,     // 6thPrize
            [720676] = 500,     // SoulCPPack
            [729246] = 900,     // 5thPrize
            [720677] = 1_000,   // BloodCPPack
            [720661] = 1_350,   // PureCPPack
            [720654] = 1_380,   // JoyCPPack
            [729247] = 2_250,   // 4thPrize
            [720666] = 2_700,   // StarCPPack
            [729248] = 4_500,   // 3rdPrize
            [720662] = 6_900,   // MysticCPPack
            [729249] = 9_000,   // 2ndPrize
            [720667] = 13_800,  // FantasyCPPack
            [729250] = 22_500,  // 1stPrize
            [729251] = 45_000,  // TopPrize
        };

        // Hair dyes from Shop.dat; the color is the hundreds digit of Hairstyle (3 black to 9 white)
        private static readonly Dictionary<uint, byte> HairDyes = new Dictionary<uint, byte>
        {
            [1060030] = 3, // BlackDye
            [1060040] = 4, // VioletDye
            [1060050] = 5, // BlueDye
            [1060060] = 6, // GreenDye
            [1060070] = 7, // BrownDye
            [1060080] = 8, // RedDye
            [1060090] = 9, // WhiteDye
        };

        // Fireworks: null text launches sky bursts (MsgItem 26); non-null also spells text (MsgName 1)
        private static readonly Dictionary<uint, string> Fireworks = new Dictionary<uint, string>
        {
            [720030] = null,           // Firework
            [720265] = null,           // ColorfulFirework
            [720601] = null,           // FestivalFirework
            [721862] = null,           // AutumnFirework
            [720031] = "Endless Love", // EndlessLove
            [720032] = "My Wish",      // MyWish
        };

        // PK-point-reducing items from itemtype.dat
        private static readonly Dictionary<uint, PkReducer> PkReducers = new Dictionary<uint, PkReducer>
        {
            [720128] = new PkReducer(30, 30, true, TimeSpan.FromHours(1)), // PenitenceAmulet
            [723727] = new PkReducer(30, 30, true, TimeSpan.FromHours(1)), // PenitenceAmulet
            [721772] = new PkReducer(10, 1, false, TimeSpan.Zero),         // AtonementPotion
            [721773] = new PkReducer(30, 1, false, TimeSpan.Zero),         // PenancePotion
            [722364] = new PkReducer(5, 1, false, TimeSpan.Zero),          // RepentPotion
            [720382] = new PkReducer(1, 1, false, TimeSpan.Zero),          // Bible
        };

        private static readonly ConcurrentDictionary<uint, DateTime> PenitenceCooldowns =
            new ConcurrentDictionary<uint, DateTime>();


        public static bool TryGetPack(uint typeID, out PackEntry[] entries) => ItemPacks.TryGetValue(typeID, out entries);
        public static bool TryGetSilverBag(uint typeID, out uint silver) => SilverBags.TryGetValue(typeID, out silver);
        public static bool TryGetConquerPointPack(uint typeID, out uint points) => ConquerPointPacks.TryGetValue(typeID, out points);
        public static bool TryGetHairDye(uint typeID, out byte color) => HairDyes.TryGetValue(typeID, out color);
        public static bool TryGetFirework(uint typeID, out string text) => Fireworks.TryGetValue(typeID, out text);
        public static bool TryGetPkReducer(uint typeID, out PkReducer reducer) => PkReducers.TryGetValue(typeID, out reducer);

        public static async Task<bool> UseAsync(Client client, uint itemID)
        {
            var character = client.Character;
            var item = client.Inventory?.Find(itemID);
            if (character == null || item == null)
                return Reject(client, itemID, 0, "not_in_inventory");

            // The client asks to use a quiver it's told to wear
            if (ItemSlots.IsArrow(item.TypeID))
                return await Equipping.EquipAsync(client, itemID, ItemPosition.LeftHand);
            if (HeavenBlessing.StoneDuration(item.TypeID) is TimeSpan blessing)
                return await PrayAsync(client, item, blessing);
            if (DoubleExp.TryGetPotion(item.TypeID, out var expPotion))
                return await UseExpPotionAsync(client, item, expPotion);
            if (Kernel.ItemTypes.TryGet(item.TypeID, out var type) && IsPotion(type))
                return await DrinkAsync(client, item, type);
            if (System.Array.IndexOf(ExpBalls, item.TypeID) >= 0)
                return await ExpBallAsync(client, item);
            if (Magic.Learning.BookOf(item.TypeID) is ushort magic)
                return await ReadBookAsync(client, item, magic);
            if (ItemPacks.TryGetValue(item.TypeID, out var pack))
                return await OpenPackAsync(client, item, pack);
            if (SilverBags.TryGetValue(item.TypeID, out var silver))
                return await OpenSilverBagAsync(client, item, silver);
            if (ConquerPointPacks.TryGetValue(item.TypeID, out var points))
                return await OpenConquerPointPackAsync(client, item, points);
            if (HairDyes.TryGetValue(item.TypeID, out var color))
                return await DyeHairAsync(client, item, color);
            if (Fireworks.TryGetValue(item.TypeID, out var fireworkText))
                return await LaunchFireworkAsync(client, item, fireworkText);
            if (PkReducers.TryGetValue(item.TypeID, out var pkReducer))
                return await ReducePkAsync(client, item, pkReducer);

            var scroll = Kernel.MapTypes.ScrollPointOf(item.TypeID);
            if (scroll == null)
                return Reject(client, itemID, item.TypeID, "not_usable");
            if (client.Life.Dead)
                return Reject(client, itemID, item.TypeID, "dead");
            if ((Kernel.MapTypes.Of(character.MapID) & (MapFlags.TeleportDisable | MapFlags.PrisonMap)) != 0)
            {
                await Talk.NoticeAsync(client, "You can't teleport out of this map.");
                return Reject(client, itemID, item.TypeID, "teleport_disabled");
            }

            uint fromMap = character.MapID;
            if (!await Movement.TeleportAsync(client, scroll.MapID, scroll.X, scroll.Y))
                return Reject(client, itemID, item.TypeID, "bad_destination");
            await UseUpAsync(client, item);
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) on map {FromMapId} and went to map {MapId}",
                character.Name, item.ItemID, item.TypeID, fromMap, scroll.MapID);
            return true;
        }

        // Stancher to Mil.Ginseng (1000000 to 1002050): their life and mana fields are the HP and MP they restore
        public static bool IsPotion(ItemType type) => type.ID / 10000 == 100 && (type.Life > 0 || type.Mana > 0);

        // Up to full HP and MP; a potion that would restore nothing is kept
        private static async Task<bool> DrinkAsync(Client client, DbItem potion, ItemType type)
        {
            var character = client.Character;
            if (client.Life.Dead)
                return Reject(client, potion.ItemID, potion.TypeID, "dead");
            if (client.Effects.Has(StatusFlags.NoPotion, DateTime.UtcNow))
                return Reject(client, potion.ItemID, potion.TypeID, "poison_star");
            ushort health = (ushort)Math.Min(Attributes.MaxHealth(client), character.HealthPoints + type.Life);
            ushort mana = (ushort)Math.Min(Attributes.MaxMana(client), character.ManaPoints + type.Mana);
            int healed = health - character.HealthPoints, restored = mana - character.ManaPoints;
            if (healed <= 0 && restored <= 0)
                return Reject(client, potion.ItemID, potion.TypeID, "full");

            await UseUpAsync(client, potion);
            character.HealthPoints = health;
            character.ManaPoints = mana;
            await client.SendAsync(new MsgUserAttrib(client.ID)
                .Append(MsgUserAttrib.AttributeType.Health, character.HealthPoints)
                .Append(MsgUserAttrib.AttributeType.Mana, character.ManaPoints));
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) and restored {Health} HP and {Mana} MP",
                character.Name, potion.ItemID, potion.TypeID, healed, restored);
            return true;
        }

        private static async Task<bool> UseExpPotionAsync(Client client, DbItem item, DoubleExp.Potion potion)
        {
            if (client.Life.Dead)
                return Reject(client, item.ItemID, item.TypeID, "dead");
            var now = DateTime.UtcNow;
            if (!DoubleExp.Apply(client.ID, potion.Percent, potion.Duration, now))
            {
                await Talk.NoticeAsync(client, "You already have a stronger EXP bonus active.");
                return Reject(client, item.ItemID, item.TypeID, "stronger_exp_bonus");
            }
            await UseUpAsync(client, item);
            await DoubleExp.SendAsync(client, now);
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) for {Percent}% EXP ({Minutes}m)",
                client.Character.Name, item.ItemID, item.TypeID, potion.Percent, potion.Duration.TotalMinutes);
            return true;
        }

        private static async Task<bool> ExpBallAsync(Client client, DbItem ball)
        {
            var character = client.Character;
            if (character.Level >= ExpBallMaxLevel)
            {
                await Talk.NoticeAsync(client, $"EXP balls can't be used from level {ExpBallMaxLevel}.");
                return Reject(client, ball.ItemID, ball.TypeID, "level");
            }
            var today = Clock.Today(DateTime.UtcNow);
            int used = ExpBallsUsedOn(character, today);
            if (used >= ExpBallsPerDay)
            {
                await Talk.NoticeAsync(client, $"You can use only {ExpBallsPerDay} EXP balls a day.");
                return Reject(client, ball.ItemID, ball.TypeID, "daily_limit");
            }
            ulong experience = Kernel.Experience.ExperienceOfTime(character.Level, character.Experience, ExperienceTable.BallTime);
            if (experience == 0)
                return Reject(client, ball.ItemID, ball.TypeID, "max_level");

            await UseUpAsync(client, ball);
            character.ExpBallDay = today;
            character.ExpBallsUsed = (byte)(used + 1);
            await Progress.AwardExperienceAsync(client, experience, "exp_ball");
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) for {Experience} experience",
                character.Name, ball.ItemID, ball.TypeID, experience);
            return true;
        }

        // The pack's own slot holds one of the items it yields
        private static async Task<bool> OpenPackAsync(Client client, DbItem pack, PackEntry[] entries)
        {
            int total = entries.Sum(e => e.Count);
            if (client.Inventory.FreeSlots < total - 1)
            {
                await Talk.NoticeAsync(client, $"You need {total - 1} free inventory slots for that.");
                return Reject(client, pack.ItemID, pack.TypeID, "inventory_full");
            }
            var resolved = new List<(ItemType Type, int Count)>(entries.Length);
            foreach (var entry in entries)
            {
                if (!Kernel.ItemTypes.TryGet(entry.TypeID, out var type))
                    return Reject(client, pack.ItemID, pack.TypeID, "unknown_pack_item");
                resolved.Add((type, entry.Count));
            }

            await UseUpAsync(client, pack);
            foreach (var (type, count) in resolved)
                for (int i = 0; i < count; i++)
                    await ItemActions.GiveAsync(client, type, "pack");
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) for {Amount} items",
                client.Character.Name, pack.ItemID, pack.TypeID, total);
            return true;
        }

        private static async Task<bool> OpenSilverBagAsync(Client client, DbItem bag, uint silver)
        {
            if (!await Progress.ChangeSilverAsync(client, silver, "money_bag"))
            {
                await Talk.NoticeAsync(client, "You can't carry any more silver.");
                return Reject(client, bag.ItemID, bag.TypeID, "silver_limit");
            }
            await UseUpAsync(client, bag);
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) for {Silver} silver",
                client.Character.Name, bag.ItemID, bag.TypeID, silver);
            return true;
        }

        private static async Task<bool> OpenConquerPointPackAsync(Client client, DbItem pack, uint points)
        {
            if (!await Progress.ChangeConquerPointsAsync(client, points, "cp_pack"))
            {
                await Talk.NoticeAsync(client, "You can't carry any more Conquer Points.");
                return Reject(client, pack.ItemID, pack.TypeID, "cp_limit");
            }
            await UseUpAsync(client, pack);
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) for {Points} CPs",
                client.Character.Name, pack.ItemID, pack.TypeID, points);
            return true;
        }

        private static async Task<bool> DyeHairAsync(Client client, DbItem dye, byte color)
        {
            var character = client.Character;
            if (client.Life.Dead)
                return Reject(client, dye.ItemID, dye.TypeID, "dead");
            ushort next = (ushort)(color * 100 + character.Hairstyle % 100);
            if (next == character.Hairstyle)
                return Reject(client, dye.ItemID, dye.TypeID, "same_hair_color");

            await UseUpAsync(client, dye);
            character.Hairstyle = next;
            var update = new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.Hairstyle, next);
            await client.SendAsync(update);
            await Movement.SendToViewAsync(client, update);
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) to dye hair to {Hairstyle}",
                character.Name, dye.ItemID, dye.TypeID, next);
            return true;
        }

        private static async Task<bool> LaunchFireworkAsync(Client client, DbItem firework, string text)
        {
            if (client.Life.Dead)
                return Reject(client, firework.ItemID, firework.TypeID, "dead");
            await UseUpAsync(client, firework);
            if (text != null)
            {
                var letters = MsgName.FireworksOf(client.ID, text);
                await client.SendAsync(letters);
                await Movement.SendToViewAsync(client, letters);
            }
            else
            {
                var burst = MsgItem.Fireworks(client.ID);
                await client.SendAsync(burst);
                await Movement.SendToViewAsync(client, burst);
            }
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) to launch fireworks",
                client.Character.Name, firework.ItemID, firework.TypeID);
            return true;
        }

        private static async Task<bool> ReducePkAsync(Client client, DbItem item, PkReducer reducer)
        {
            var character = client.Character;
            if (client.Life.Dead)
                return Reject(client, item.ItemID, item.TypeID, "dead");
            if (reducer.ForbidJail && (Kernel.MapTypes.Of(character.MapID) & MapFlags.PrisonMap) != 0)
            {
                await Talk.NoticeAsync(client, "You can't use that in jail.");
                return Reject(client, item.ItemID, item.TypeID, "in_jail");
            }
            if (character.KillPoints < reducer.MinPoints)
            {
                await Talk.NoticeAsync(client, reducer.MinPoints > 1
                    ? $"You need at least {reducer.MinPoints} PK points to use that."
                    : "You don't have any PK points to reduce.");
                return Reject(client, item.ItemID, item.TypeID, "pk_points");
            }
            var now = DateTime.UtcNow;
            if (reducer.Cooldown > TimeSpan.Zero &&
                PenitenceCooldowns.TryGetValue(client.ID, out var until) && until > now)
            {
                int minutes = Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
                await Talk.NoticeAsync(client, $"You can use a Penitence Amulet again in {minutes} minute(s).");
                return Reject(client, item.ItemID, item.TypeID, "cooldown");
            }

            await UseUpAsync(client, item);
            if (reducer.Cooldown > TimeSpan.Zero)
                PenitenceCooldowns[client.ID] = now + reducer.Cooldown;
            await Pking.ChangePointsAsync(client, -reducer.Amount, "pk_item");
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) to reduce PK points by {Amount}",
                character.Name, item.ItemID, item.TypeID, reducer.Amount);
            return true;
        }


        // A book that can't teach its reader stays in the inventory
        private static async Task<bool> ReadBookAsync(Client client, DbItem book, ushort magic)
        {
            string reason = Magic.Learning.Unmet(client, magic);
            if (reason != null)
            {
                await Talk.NoticeAsync(client, Magic.Learning.Explain(reason, magic));
                return Reject(client, book.ItemID, book.TypeID, reason);
            }

            await UseUpAsync(client, book);
            await Magic.Learning.LearnAsync(client, magic, "book");
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) to learn magic {MagicType}",
                client.Character.Name, book.ItemID, book.TypeID, magic);
            await Talk.NoticeAsync(client, $"You have learned {Magic.Learning.NameOf(magic)}.");
            return true;
        }

        private static async Task<bool> PrayAsync(Client client, DbItem stone, TimeSpan blessing)
        {
            if (client.Life.Dead)
                return Reject(client, stone.ItemID, stone.TypeID, "dead");
            await UseUpAsync(client, stone);
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) for heaven blessing",
                client.Character.Name, stone.ItemID, stone.TypeID);
            await HeavenBlessing.AddAsync(client, blessing, "praying_stone");
            return true;
        }

        private static async Task UseUpAsync(Client client, DbItem item)
        {
            client.Inventory.Remove(item);
            await ItemsRepository.DeleteAsync(item.ItemID);
            await client.SendAsync(MsgItem.Removed(item.ItemID));
        }

        private static bool Reject(Client client, uint itemID, uint typeID, string reason)
        {
            LogEvents.For(typeof(Using), LogEvents.ItemUseRejected).Warning(
                "{Name} can't use item {ItemId} (type {TypeId}): {Reason}",
                client.Character?.Name, itemID, typeID, reason);
            return false;
        }
    }
}
