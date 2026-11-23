namespace Comet.Game.Chat
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Sockets;

    // Chat lines starting with '/'. Staff lines are all commands; a player's line is a command
    // only if it names one open to players, and otherwise stays ordinary chat.
    public static class Commands
    {
        public const char Prefix = '/';

        // A chat line holds 255 bytes
        private const int LineLength = 200;

        private delegate Task<bool> Handler(Client client, string[] args, string rest);

        private sealed record Command(string[] Names, Authority Minimum, string Usage, Handler Run);

        private static readonly Command[] All =
        {
            new Command(new[] { "help" }, Authority.Player, "/help", HelpAsync),
            new Command(new[] { "drop" }, Authority.Player, "/drop <silver>", DropAsync),
            new Command(new[] { "closebooth" }, Authority.Player, "/closebooth", CloseBoothAsync),
            new Command(new[] { "online" }, Authority.Moderator, "/online", OnlineAsync),
            new Command(new[] { "where" }, Authority.Moderator, "/where [name]", WhereAsync),
            new Command(new[] { "kick" }, Authority.Moderator, "/kick <name>", KickAsync),
            new Command(new[] { "ban" }, Authority.Moderator, "/ban <name> [reason]", BanAsync),
            new Command(new[] { "unban" }, Authority.Moderator, "/unban <account>", UnbanAsync),
            new Command(new[] { "mute" }, Authority.Moderator, "/mute <name> <minutes>", MuteAsync),
            new Command(new[] { "unmute" }, Authority.Moderator, "/unmute <name>", UnmuteAsync),
            new Command(new[] { "teleport", "tp" }, Authority.GameMaster, TeleportUsage, TeleportAsync),
            new Command(new[] { "goto" }, Authority.GameMaster, "/goto <name>", GotoAsync),
            new Command(new[] { "grab" }, Authority.GameMaster, "/grab <name>", GrabAsync),
            new Command(new[] { "broadcast", "bc" }, Authority.GameMaster, "/bc <text>", BroadcastAsync),
            new Command(new[] { "exp" }, Authority.GameMaster, "/exp <amount>", ExperienceAsync),
            new Command(new[] { "level" }, Authority.GameMaster, "/level <level>", LevelAsync),
            new Command(new[] { "class" }, Authority.GameMaster, "/class <class>", ClassAsync),
            new Command(new[] { "silver" }, Authority.GameMaster, "/silver <+/-amount>", SilverAsync),
            new Command(new[] { "item" }, Authority.GameMaster, "/item <type> [count]", ItemAsync),
            new Command(new[] { "steed" }, Authority.GameMaster, "/steed [plus]", SteedAsync),
            new Command(new[] { "attr" }, Authority.GameMaster, "/attr <str|agi|vit|spi|points> <value|+n|-n>", StatAsync),
            new Command(new[] { "rawattr" }, Authority.GameMaster, "/rawattr <type> <value>", AttributeAsync),
            new Command(new[] { "pk" }, Authority.GameMaster, "/pk <points> [name]", PkPointsAsync),
            new Command(new[] { "flash" }, Authority.GameMaster, "/flash [seconds]", FlashAsync),
            new Command(new[] { "jail" }, Authority.GameMaster, "/jail <name>", JailAsync),
            new Command(new[] { "learn" }, Authority.GameMaster, "/learn <magic type> [level]", LearnAsync),
            new Command(new[] { "prof" }, Authority.GameMaster, "/prof <weapon type> <level>", ProficiencyAsync),
            new Command(new[] { "xp" }, Authority.GameMaster, "/xp", XpAsync),
            new Command(new[] { "restore" }, Authority.GameMaster, "/restore", RestoreAsync),
            new Command(new[] { "window" }, Authority.GameMaster, WindowUsage, WindowAsync),
            new Command(new[] { "arenaseason" }, Authority.GameMaster, "/arenaseason", ArenaSeasonAsync),
        };

        public static bool IsCommand(Client client, string text) =>
            text.Length > 1 && text[0] == Prefix &&
            (client.Authority >= Authority.Moderator || Find(client.Authority, Parse(text).Name) != null);

        public static (string Name, string[] Args, string Text) Parse(string text)
        {
            var line = text.TrimStart(Prefix).Trim();
            int space = line.IndexOfAny(new[] { ' ', '\t' });
            string name = space < 0 ? line : line.Substring(0, space);
            string rest = space < 0 ? string.Empty : line.Substring(space + 1).Trim();
            var args = rest.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return (name.ToLowerInvariant(), args, rest);
        }

        public static async Task RunAsync(Client client, string text)
        {
            var (name, args, rest) = Parse(text);
            var command = Find(client.Authority, name);
            string result;
            if (command == null)
            {
                result = "unknown";
                await Talk.NoticeAsync(client, $"Unknown command /{name}. Try /help.");
            }
            else
            {
                result = await command.Run(client, args, rest) ? "ok" : "failed";
            }

            var eventName = command?.Minimum == Authority.Player ? LogEvents.ChatCommand : LogEvents.GmCommand;
            LogEvents.For(typeof(Commands), eventName).Information(
                "{Name} ran /{Command} {Arguments}: {Result}",
                client.Character.Name, name, rest, result);
        }

        // Commands above the player's authority are reported as unknown
        private static Command Find(Authority authority, string name) =>
            All.FirstOrDefault(c => c.Names.Contains(name) && authority >= c.Minimum);

        private static async Task<bool> UsageAsync(Client client, string usage)
        {
            await Talk.NoticeAsync(client, "Usage: " + usage);
            return false;
        }

        private static async Task<bool> NotOnlineAsync(Client client, string name)
        {
            await Talk.NoticeAsync(client, $"{name} is not online.");
            return false;
        }

        public static bool TryFind(string name, out Client player)
        {
            if (Kernel.Players.TryGet(name, out player) && player.Character != null)
                return true;
            var matches = Kernel.Players.Values.Where(p =>
                p.Character?.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase) == true).Take(2).ToList();
            player = matches.Count == 1 ? matches[0] : null;
            return player != null;
        }

        private static async Task<bool> HelpAsync(Client client, string[] args, string rest)
        {
            var usages = All.Where(c => client.Authority >= c.Minimum).Select(c => c.Usage);
            foreach (var line in Lines("Commands:", usages))
                await Talk.NoticeAsync(client, line);
            return true;
        }

        // The client won't move while it thinks it's vending; a teleport onto its own cell ends
        // that (seen in the VM).
        private static async Task<bool> CloseBoothAsync(Client client, string[] args, string rest)
        {
            if (client.Booth == null)
            {
                await Talk.NoticeAsync(client, "You have no booth open.");
                return false;
            }
            await Vending.CloseAsync(client, "command");
            var character = client.Character;
            await Movement.TeleportAsync(client, character.MapID, character.X, character.Y);
            await Talk.NoticeAsync(client, "Your booth is closed.");
            return true;
        }

        private static async Task<bool> OnlineAsync(Client client, string[] args, string rest)
        {
            var names = Kernel.Players.Values
                .Select(p => p.Character?.Name).Where(n => n != null)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var line in Lines($"{names.Count} online:", names))
                await Talk.NoticeAsync(client, line);
            return true;
        }

        private static async Task<bool> WhereAsync(Client client, string[] args, string rest)
        {
            var target = client;
            if (args.Length > 0 && !TryFind(args[0], out target))
                return await NotOnlineAsync(client, args[0]);
            var character = target.Character;
            await Talk.NoticeAsync(client,
                $"{character.Name} is on map {character.MapID} at {character.X},{character.Y}.");
            return true;
        }

        private static async Task<bool> KickAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 1)
                return await UsageAsync(client, "/kick <name>");
            if (!TryFind(args[0], out var target))
                return await NotOnlineAsync(client, args[0]);
            if (target != client && target.Authority >= client.Authority)
            {
                await Talk.NoticeAsync(client, $"You can't kick {target.Character.Name}.");
                return false;
            }

            await Talk.NoticeAsync(client, $"Kicked {target.Character.Name}.");
            target.Disconnect(DisconnectReasons.Kicked);
            return true;
        }

        // Like /kick, only on players of lower authority
        private static async Task<bool> OutranksAsync(Client client, Client target, string verb)
        {
            if (target.Authority < client.Authority)
                return true;
            await Talk.NoticeAsync(client, $"You can't {verb} {target.Character.Name}.");
            return false;
        }

        private static async Task<bool> BanAsync(Client client, string[] args, string rest)
        {
            if (args.Length < 1)
                return await UsageAsync(client, "/ban <name> [reason]");
            if (!TryFind(args[0], out var target))
                return await NotOnlineAsync(client, args[0]);
            if (!await OutranksAsync(client, target, "ban"))
                return false;

            var name = target.Character.Name;
            if (!await Database.Repositories.AccountsRepository.BanAsync(target.Character.AccountID))
            {
                await Talk.NoticeAsync(client, $"Can't ban {name}: account {target.Character.AccountID} not found.");
                return false;
            }
            string reason = rest.Substring(args[0].Length).Trim();
            await Talk.NoticeAsync(target, reason.Length > 0 ? "You have been banned: " + reason : "You have been banned.");
            await Talk.NoticeAsync(client, $"Banned {name}.");
            target.Disconnect(DisconnectReasons.Banned);
            return true;
        }

        private static async Task<bool> UnbanAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 1)
                return await UsageAsync(client, "/unban <account>");
            switch (await Database.Repositories.AccountsRepository.UnbanAsync(args[0]))
            {
                case Database.Repositories.AccountsRepository.UnbanResult.Unbanned:
                    await Talk.NoticeAsync(client, $"Unbanned account {args[0]}.");
                    return true;
                case Database.Repositories.AccountsRepository.UnbanResult.NotBanned:
                    await Talk.NoticeAsync(client, $"Account {args[0]} isn't banned.");
                    return false;
                default:
                    await Talk.NoticeAsync(client, $"There's no account {args[0]}.");
                    return false;
            }
        }

        // Ten years; much more would overflow DateTime
        private const int MaxMuteMinutes = 10 * 365 * 24 * 60;

        private static async Task<bool> MuteAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 2 || !int.TryParse(args[1], out var minutes) || minutes < 1 || minutes > MaxMuteMinutes)
                return await UsageAsync(client, $"/mute <name> <minutes 1-{MaxMuteMinutes}>");
            if (!TryFind(args[0], out var target))
                return await NotOnlineAsync(client, args[0]);
            if (!await OutranksAsync(client, target, "mute"))
                return false;

            var character = target.Character;
            character.MutedUntil = DateTime.UtcNow.AddMinutes(minutes);
            await target.SaveAsync("mute");
            await Talk.NoticeAsync(target, $"You have been muted for {minutes} minutes.");
            await Talk.NoticeAsync(client, $"Muted {character.Name} for {minutes} minutes.");
            return true;
        }

        private static async Task<bool> UnmuteAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 1)
                return await UsageAsync(client, "/unmute <name>");
            if (!TryFind(args[0], out var target))
                return await NotOnlineAsync(client, args[0]);
            if (!await OutranksAsync(client, target, "unmute"))
                return false;

            var character = target.Character;
            if (!Talk.IsMuted(character, DateTime.UtcNow))
            {
                await Talk.NoticeAsync(client, $"{character.Name} isn't muted.");
                return false;
            }
            character.MutedUntil = null;
            await target.SaveAsync("mute");
            await Talk.NoticeAsync(target, "You can chat again.");
            await Talk.NoticeAsync(client, $"Unmuted {character.Name}.");
            return true;
        }

        private const string TeleportUsage = "/tp <place|map> [x y], /tp <x> <y>";

        // /tp market goes to the map's own portal, /tp market 200 200 to a cell on it, and
        // /tp 200 200 to a cell on this map. Places are names from Data/maps.json or map IDs.
        private static async Task<bool> TeleportAsync(Client client, string[] args, string rest)
        {
            var character = client.Character;
            if (args.Length == 2 && ushort.TryParse(args[0], out var hereX) && ushort.TryParse(args[1], out var hereY))
                return await MoveAsync(client, character.MapID, hereX, hereY);
            if (args.Length != 1 && args.Length != 3)
                return await UsageAsync(client, TeleportUsage);

            uint map;
            if (!uint.TryParse(args[0], out map))
            {
                var named = Kernel.MapTypes.Find(args[0]);
                if (named == null)
                {
                    var places = Kernel.MapTypes.Named.Select(m => m.Aliases?.Count > 0 ? $"{m.Name} ({string.Join(", ", m.Aliases)})" : m.Name);
                    foreach (var line in Lines($"No place {args[0]}. Places:", places))
                        await Talk.NoticeAsync(client, line);
                    return false;
                }
                map = named.MapID;
            }

            if (args.Length == 3)
            {
                if (!ushort.TryParse(args[1], out var x) || !ushort.TryParse(args[2], out var y))
                    return await UsageAsync(client, TeleportUsage);
                return await MoveAsync(client, map, x, y);
            }

            var point = Kernel.MapTypes.PortalOf(map);
            if (point == null && Kernel.Portals.Into(map) is PortalTarget into)
                point = new MapPoint(map, into.X, into.Y);
            if (point == null)
            {
                await Talk.NoticeAsync(client, $"Map {map} has no default point. Try /tp {args[0]} <x> <y>.");
                return false;
            }
            return await MoveAsync(client, point.MapID, point.X, point.Y);
        }

        private static async Task<bool> GotoAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 1)
                return await UsageAsync(client, "/goto <name>");
            if (!TryFind(args[0], out var target))
                return await NotOnlineAsync(client, args[0]);
            var character = target.Character;
            return await MoveAsync(client, character.MapID, character.X, character.Y);
        }

        private static async Task<bool> GrabAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 1)
                return await UsageAsync(client, "/grab <name>");
            if (!TryFind(args[0], out var target))
                return await NotOnlineAsync(client, args[0]);
            var character = client.Character;
            if (await Movement.TeleportAsync(target, character.MapID, character.X, character.Y))
                return true;
            await Talk.NoticeAsync(client, $"Can't grab {target.Character.Name}.");
            return false;
        }

        private static async Task<bool> BroadcastAsync(Client client, string[] args, string rest)
        {
            if (rest.Length == 0)
                return await UsageAsync(client, "/bc <text>");
            var message = new MsgTalk(0, MsgTalk.TalkChannel.Center, rest);
            foreach (var player in Kernel.Players.Values)
                await player.SendAsync(message);
            return true;
        }

        private static async Task<bool> ExperienceAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 1 || !ulong.TryParse(args[0], out var amount) || amount == 0)
                return await UsageAsync(client, "/exp <amount>");
            await Progress.AwardExperienceAsync(client, amount, "gm");
            return true;
        }

        private const string WindowUsage = "/window <number> [custom]";

        // Asks the client to open one of its windows, to find which number opens which: MsgAction
        // 126, or 160 with "custom"
        private static async Task<bool> WindowAsync(Client client, string[] args, string rest)
        {
            bool custom = args.Length == 2 && args[1] == "custom";
            if (args.Length is < 1 or > 2 || args.Length == 2 && !custom || !uint.TryParse(args[0], out var window))
                return await UsageAsync(client, WindowUsage);
            var action = custom ? MsgAction.ActionType.ClientCustomDialog : MsgAction.ActionType.ClientDialog;
            await client.SendAsync(MsgAction.OpenWindow(client.ID, window, client.Character.X, client.Character.Y, action));
            await Talk.NoticeAsync(client, $"Asked the client to open window {window} ({(int)action}).");
            return true;
        }

        private static async Task<bool> ClassAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 1 || !byte.TryParse(args[0], out var currentClass))
                return await UsageAsync(client, "/class <class>");
            if (!Promotion.Exists(currentClass))
            {
                await Talk.NoticeAsync(client, "Classes are 10-15, 20-25, 40-45, 50-55, 60-65, 70-75, 100, 101, 132-135 and 142-145.");
                return false;
            }
            await Progress.SetClassAsync(client, currentClass);
            return true;
        }

        private static async Task<bool> LevelAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 1 || !byte.TryParse(args[0], out var level))
                return await UsageAsync(client, "/level <level>");
            if (await Progress.SetLevelAsync(client, level, "gm"))
                return true;
            await Talk.NoticeAsync(client, $"Levels go from 1 to {Kernel.Experience.MaxLevel}.");
            return false;
        }

        private static async Task<bool> SilverAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 1 || !long.TryParse(args[0], out var amount) || amount == 0)
                return await UsageAsync(client, "/silver <+/-amount>");
            if (await Progress.ChangeSilverAsync(client, amount, "gm"))
                return true;
            await Talk.NoticeAsync(client, $"Silver must stay between 0 and {uint.MaxValue}.");
            return false;
        }

        private static readonly Dictionary<string, Progress.Stat> Stats = new Dictionary<string, Progress.Stat>
        {
            ["str"] = Progress.Stat.Strength, ["agi"] = Progress.Stat.Agility,
            ["vit"] = Progress.Stat.Vitality, ["spi"] = Progress.Stat.Spirit,
            ["points"] = Progress.Stat.Points,
        };

        private static async Task<bool> StatAsync(Client client, string[] args, string rest)
        {
            const string usage = "/attr <str|agi|vit|spi|points> <value|+n|-n>";
            if (args.Length != 2 || !Stats.TryGetValue(args[0].ToLowerInvariant(), out var stat) ||
                !int.TryParse(args[1], out var amount))
                return await UsageAsync(client, usage);
            bool relative = args[1][0] == '+' || args[1][0] == '-';
            await Progress.SetStatAsync(client, stat, amount, relative, "gm");
            return true;
        }

        // Sends any MsgUserAttrib as is, to find out what the client does with a value. Nothing is saved.
        private static async Task<bool> AttributeAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 2 || !uint.TryParse(args[0], out var type) || !TryParseNumber(args[1], out var value))
                return await UsageAsync(client, "/rawattr <type> <value>");
            var attribute = new MsgUserAttrib(client.ID, (MsgUserAttrib.AttributeType)type, value);
            await client.SendAsync(attribute);
            await Movement.SendToViewAsync(client, attribute);
            return true;
        }

        private static bool TryParseNumber(string text, out ulong value) =>
            text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ulong.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out value)
                : ulong.TryParse(text, out value);

        private const uint SteedType = 300000;

        private static async Task<bool> SteedAsync(Client client, string[] args, string rest)
        {
            byte plus = 0;
            if (args.Length > 1 || (args.Length == 1 && (!byte.TryParse(args[0], out plus) || plus > 12)))
                return await UsageAsync(client, "/steed [plus 0-12]");
            if (!Kernel.ItemTypes.TryGet(SteedType, out var type))
            {
                await Talk.NoticeAsync(client, $"There's no item type {SteedType}.");
                return false;
            }
            if (await ItemActions.GiveAsync(client, type, "gm", plus) != null)
                return true;
            await Talk.NoticeAsync(client, "Your inventory is full.");
            return false;
        }

        private static async Task<bool> ItemAsync(Client client, string[] args, string rest)
        {
            int count = 1;
            if (args.Length < 1 || args.Length > 2 || !uint.TryParse(args[0], out var typeID) ||
                (args.Length == 2 && (!int.TryParse(args[1], out count) || count < 1 || count > Inventory.Capacity)))
                return await UsageAsync(client, "/item <type> [count]");
            if (!Kernel.ItemTypes.TryGet(typeID, out var type))
            {
                await Talk.NoticeAsync(client, $"There's no item type {typeID}.");
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (await ItemActions.GiveAsync(client, type, "gm") == null)
                {
                    await Talk.NoticeAsync(client, "Your inventory is full.");
                    return i > 0;
                }
            }
            return true;
        }

        private static async Task<bool> LearnAsync(Client client, string[] args, string rest)
        {
            ushort level = 0;
            if (args.Length < 1 || args.Length > 2 || !ushort.TryParse(args[0], out var type) ||
                (args.Length == 2 && !ushort.TryParse(args[1], out level)))
                return await UsageAsync(client, "/learn <magic type> [level]");
            if (await Magic.Casting.LearnAsync(client, type, level, "gm"))
                return true;
            await Talk.NoticeAsync(client, Kernel.Magics.Exists(type)
                ? $"Magic {type} goes up to level {Kernel.Magics.MaxLevel(type)}."
                : $"No magic type {type} in magictype.dat.");
            return false;
        }

        // Weapon types are item type / 1000: 410 blade, 420 sword, 480 club, 500 bow, 510 glaive
        private static async Task<bool> ProficiencyAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 2 || !ushort.TryParse(args[0], out var type) || type == 0 ||
                !ushort.TryParse(args[1], out var level) || level > Kernel.Proficiencies.MaxLevel)
                return await UsageAsync(client, $"/prof <weapon type> <level up to {Kernel.Proficiencies.MaxLevel}>");
            client.Skills.TryGetProficiency(type, out var current);
            await Magic.Proficiency.SetAsync(client, type, new Magic.SkillLevel(level, 0), current?.Level ?? 0, "gm");
            return true;
        }

        private static async Task<bool> XpAsync(Client client, string[] args, string rest)
        {
            await Magic.Recovery.AddXpAsync(client, Magic.Energy.MaxXp, DateTime.UtcNow);
            return true;
        }

        private static async Task<bool> RestoreAsync(Client client, string[] args, string rest)
        {
            var character = client.Character;
            if (client.Life.Dead)
                return false;
            character.HealthPoints = Attributes.MaxHealth(client);
            character.ManaPoints = Attributes.MaxMana(client);
            client.Energy.Stamina = HeavenBlessing.MaxStamina(client);
            // The maximums first, so the client doesn't cut health or mana to an older one
            await client.SendAsync(new MsgUserAttrib(client.ID)
                .Append(MsgUserAttrib.AttributeType.MaxHealth, character.HealthPoints)
                .Append(MsgUserAttrib.AttributeType.Health, character.HealthPoints)
                .Append(MsgUserAttrib.AttributeType.MaxMana, character.ManaPoints)
                .Append(MsgUserAttrib.AttributeType.Mana, character.ManaPoints)
                .Append(MsgUserAttrib.AttributeType.Stamina, client.Energy.Stamina));
            return true;
        }

        private static async Task<bool> PkPointsAsync(Client client, string[] args, string rest)
        {
            var target = client;
            if (args.Length < 1 || args.Length > 2 || !ushort.TryParse(args[0], out var points))
                return await UsageAsync(client, "/pk <points> [name]");
            if (args.Length == 2 && !TryFind(args[1], out target))
                return await NotOnlineAsync(client, args[1]);
            await Pk.Pking.ChangePointsAsync(target, points - target.Character.KillPoints, "gm");
            await Talk.NoticeAsync(client, $"{target.Character.Name} has {points} PK points.");
            return true;
        }

        private static async Task<bool> FlashAsync(Client client, string[] args, string rest)
        {
            int seconds = (int)Pk.Pking.FlashTime.TotalSeconds;
            if (args.Length > 1 || (args.Length == 1 && (!int.TryParse(args[0], out seconds) || seconds < 1)))
                return await UsageAsync(client, "/flash [seconds]");
            await Pk.Pking.FlashAsync(client, TimeSpan.FromSeconds(seconds), "gm");
            return true;
        }

        private static async Task<bool> JailAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 1)
                return await UsageAsync(client, "/jail <name>");
            if (!TryFind(args[0], out var target))
                return await NotOnlineAsync(client, args[0]);
            if (await Pk.Jail.SendAsync(target, "gm"))
                return true;
            await Talk.NoticeAsync(client, $"Can't send {target.Character.Name} to jail: map {Pk.Jail.MapID} isn't loaded.");
            return false;
        }

        private static async Task<bool> DropAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 1 || !uint.TryParse(args[0], out var amount) || amount == 0)
                return await UsageAsync(client, "/drop <silver>");
            if (amount > client.Character.Silver)
            {
                await Talk.NoticeAsync(client, $"You have only {client.Character.Silver} silver.");
                return false;
            }
            if (await Ground.DropSilverAsync(client, amount))
                return true;
            await Talk.NoticeAsync(client, "There's no room to drop it here.");
            return false;
        }

        private static async Task<bool> ArenaSeasonAsync(Client client, string[] args, string rest)
        {
            var (season, top, reset) = await Arena.Qualifier.ResetSeasonAsync();
            await Talk.NoticeAsync(client, top > 0
                ? $"Arena season {season} saved with top {top}; reset {reset} records."
                : $"Arena season reset {reset} records (nobody fought).");
            return true;
        }

        private static async Task<bool> MoveAsync(Client client, uint map, ushort x, ushort y)
        {
            if (await Movement.TeleportAsync(client, map, x, y))
                return true;
            await Talk.NoticeAsync(client, $"Can't teleport to map {map} at {x},{y}.");
            return false;
        }

        public static IEnumerable<string> Lines(string title, IEnumerable<string> items)
        {
            var line = new StringBuilder(title);
            bool first = true;
            foreach (var item in items)
            {
                if (line.Length > 0 && line.Length + item.Length + 2 > LineLength)
                {
                    yield return line.ToString();
                    line.Clear();
                    first = true;
                }
                line.Append(line.Length == 0 ? "" : first ? " " : ", ").Append(item);
                first = false;
            }
            yield return line.ToString();
        }
    }
}
