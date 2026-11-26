namespace Fetih.Game.States
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Database.Repositories;
    using Fetih.Game.Items;
    using Fetih.Game.Packets;

    // A class is a line (tens; every taoist class from 100 is one line) and a rank (ones).
    // Each rank up asks for a level, and the last three for an item. Taoists choose water or
    // fire when they become rank 2: 101 goes to 132 or 142.
    public static class Promotion
    {
        public const byte MaxRank = 5;
        public const uint Emerald = 1080001, Meteor = 1088001, MoonBox = 721080;
        public const byte Taoist = 101, WaterTaoist = 132, FireTaoist = 142;

        private static readonly byte[] Levels = { 15, 40, 70, 100, 110 };
        private static readonly uint[] Items = { 0, 0, Emerald, Meteor, MoonBox };

        private static readonly Dictionary<byte, string> Names = new Dictionary<byte, string>
        {
            [10] = "Intern Trojan", [11] = "Trojan", [12] = "Veteran Trojan", [13] = "Tiger Trojan",
            [14] = "Dragon Trojan", [15] = "Trojan Master",
            [20] = "Intern Warrior", [21] = "Warrior", [22] = "Brass Warrior", [23] = "Silver Warrior",
            [24] = "Gold Warrior", [25] = "Warrior Master",
            [40] = "Intern Archer", [41] = "Archer", [42] = "Eagle Archer", [43] = "Tiger Archer",
            [44] = "Dragon Archer", [45] = "Archer Master",
            [50] = "Intern Ninja", [51] = "Ninja", [52] = "Middle Ninja", [53] = "Dark Ninja",
            [54] = "Mystic Ninja", [55] = "Ninja Master",
            [60] = "Intern Monk", [61] = "Monk", [62] = "Dhyana Monk", [63] = "Dharma Monk",
            [64] = "Prajna Monk", [65] = "Nirvana Monk",
            [70] = "Intern Pirate", [71] = "Pirate", [72] = "Pirate Gunner", [73] = "Quartermaster",
            [74] = "Pirate Captain", [75] = "Pirate Lord",
            [100] = "Intern Taoist", [101] = "Taoist",
            [132] = "Water Taoist", [133] = "Water Wizard", [134] = "Water Master", [135] = "Water Saint",
            [142] = "Fire Taoist", [143] = "Fire Wizard", [144] = "Fire Master", [145] = "Fire Saint",
        };

        public static int Line(byte currentClass) => currentClass >= 100 ? 10 : currentClass / 10;

        public static int Rank(byte currentClass) => currentClass % 10;

        public static string Name(byte currentClass) =>
            Names.TryGetValue(currentClass, out var name) ? name : $"class {currentClass}";

        public static bool Exists(byte currentClass) => Names.ContainsKey(currentClass);

        public static bool ChoosesElement(byte currentClass) => currentClass == Taoist;

        // The class a promotion leads to; a taoist's second names the element chosen
        public static byte? Next(byte currentClass, byte element = 0)
        {
            if (Rank(currentClass) >= MaxRank || !Exists(currentClass))
                return null;
            if (ChoosesElement(currentClass))
                return element is WaterTaoist or FireTaoist ? element : null;
            return element == 0 ? (byte)(currentClass + 1) : null;
        }

        public static byte RequiredLevel(byte currentClass) => Levels[Rank(currentClass)];

        // The item the next promotion takes, or 0
        public static uint RequiredItem(byte currentClass) => Items[Rank(currentClass)];

        // Why the player can't be promoted to the class, or null
        public static string Unmet(Client client, byte line, byte element)
        {
            var character = client.Character;
            if (Line(character.CurrentClass) != line)
                return "other_line";
            if (Rank(character.CurrentClass) >= MaxRank)
                return "max_rank";
            if (Next(character.CurrentClass, element) == null)
                return "no_choice";
            if (character.Level < RequiredLevel(character.CurrentClass))
                return "level";
            uint item = RequiredItem(character.CurrentClass);
            if (item != 0 && client.Inventory.Items.All(i => i.TypeID != item))
                return "no_item";
            return null;
        }

        // Promotes the player at a master of the line, taking the item it costs; null when done,
        // or why not
        public static async Task<string> PromoteAsync(Client client, byte line, byte element, string npc)
        {
            var character = client.Character;
            if (character == null)
                return "not_found";
            byte from = character.CurrentClass;
            string reason = Unmet(client, line, element);
            uint itemType = reason == null ? RequiredItem(from) : 0;
            var item = itemType == 0 ? null : client.Inventory.Items.FirstOrDefault(i => i.TypeID == itemType);
            if (itemType != 0 && (item == null || !client.Inventory.TakeOne(item)))
                reason = "no_item";
            if (reason != null)
            {
                LogEvents.For(typeof(Promotion), LogEvents.CharacterPromotionRejected).Information(
                    "{Name} can't be promoted from class {FromClass} at NPC {Npc}: {Reason}",
                    character.Name, from, npc, reason);
                return reason;
            }

            if (item != null)
                await ItemActions.SaveTakenAsync(client, item);

            byte to = Next(from, element).Value;
            await Progress.SetClassAsync(client, to);
            LogEvents.For(typeof(Promotion), LogEvents.CharacterPromoted).Information(
                "{Name} was promoted from class {FromClass} to {Class} at NPC {Npc} for item {ItemTypeId}",
                character.Name, from, to, npc, itemType);
            await client.SaveAsync("promotion");
            return null;
        }
    }
}
