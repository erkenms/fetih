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
            new Command(new[] { "teleport", "tp" }, Authority.GameMaster, "/tp <map> <x> <y>", TeleportAsync),
            new Command(new[] { "goto" }, Authority.GameMaster, "/goto <name>", GotoAsync),
            new Command(new[] { "grab" }, Authority.GameMaster, "/grab <name>", GrabAsync),
            new Command(new[] { "broadcast", "bc" }, Authority.GameMaster, "/bc <text>", BroadcastAsync),
            new Command(new[] { "exp" }, Authority.GameMaster, "/exp <amount>", ExperienceAsync),
            new Command(new[] { "level" }, Authority.GameMaster, "/level <level>", LevelAsync),
            new Command(new[] { "silver" }, Authority.GameMaster, "/silver <+/-amount>", SilverAsync),
            new Command(new[] { "item" }, Authority.GameMaster, "/item <type> [count]", ItemAsync),
            new Command(new[] { "attr" }, Authority.GameMaster, "/attr <type> <value>", AttributeAsync),
            new Command(new[] { "pk" }, Authority.GameMaster, "/pk <points> [name]", PkPointsAsync),
            new Command(new[] { "flash" }, Authority.GameMaster, "/flash [seconds]", FlashAsync),
            new Command(new[] { "jail" }, Authority.GameMaster, "/jail <name>", JailAsync),
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

        // The full name, or the start of exactly one online player's name
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

        // The client shows no button to close a booth yet. It won't move while it thinks it's
        // vending; a teleport onto its own cell ends that (seen in the VM).
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

        private static async Task<bool> TeleportAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 3 || !uint.TryParse(args[0], out var map) ||
                !ushort.TryParse(args[1], out var x) || !ushort.TryParse(args[2], out var y))
                return await UsageAsync(client, "/tp <map> <x> <y>");
            return await MoveAsync(client, map, x, y);
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

        // Sends any MsgUserAttrib as is, for finding out what the client does with a value.
        // Values may be hex (0x...). Nothing is saved.
        private static async Task<bool> AttributeAsync(Client client, string[] args, string rest)
        {
            if (args.Length != 2 || !uint.TryParse(args[0], out var type) || !TryParseNumber(args[1], out var value))
                return await UsageAsync(client, "/attr <type> <value>");
            var attribute = new MsgUserAttrib(client.ID, (MsgUserAttrib.AttributeType)type, value);
            await client.SendAsync(attribute);
            await Movement.SendToViewAsync(client, attribute);
            return true;
        }

        private static bool TryParseNumber(string text, out ulong value) =>
            text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ulong.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out value)
                : ulong.TryParse(text, out value);

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
