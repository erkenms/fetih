namespace Comet.Game.States
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Magic;
    using Comet.Game.Packets;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgUserAttrib;
    using Operation = Comet.Game.Magic.MagicTypeOpTable.Operation;

    // A master of a line (rank 5) at level 120, a water saint at 110, starts a new life at
    // level 15 in a class they choose: Eternity's first rebirth takes a Celestial Stone,
    // Alex's second an Exemption Token. What happens to the spells is the client's
    // magictypeop.dat; worn gear goes back to its lowest level.
    public static class Rebirth
    {
        public const byte MaxRebirths = 2, NewLevel = 15, WaterSaint = 135;
        public const uint CelestialStone = 721259, ExemptionToken = 723701;

        // What a new life may be, in the order the NPCs offer them
        public static readonly byte[] Classes = { 11, 21, 41, 51, 61, 71, 132, 142 };

        public static byte RequiredLevel(byte currentClass) => currentClass == WaterSaint ? (byte)110 : (byte)120;

        // The item rebirth number life (1 or 2) takes
        public static uint RequiredItem(byte life) => life == 1 ? CelestialStone : ExemptionToken;

        // The attribute points a life reborn at the level gives besides the starting ones: 30
        // for the first and 52 for the second, and up to 55 more by the levels past 120 (two
        // levels a step past 110 for a water saint), as many as at 130.
        public static int LifePoints(byte life, byte currentClass, byte level)
        {
            int past = Math.Max(0, currentClass == WaterSaint ? (level - 110) / 2 : level - 120);
            return (life == 1 ? 30 : 52) + Math.Min((1 + past) * past / 2, 55);
        }

        // Why the character can't be reborn the life-th time into the class, or null
        public static string Unmet(Client client, byte life, byte newClass)
        {
            var character = client.Character;
            if (character.Rebirths >= MaxRebirths)
                return "max_rebirths";
            if (character.Rebirths != life - 1)
                return "other_rebirth";
            if (Promotion.Rank(character.CurrentClass) < Promotion.MaxRank)
                return "promotion";
            if (character.Level < RequiredLevel(character.CurrentClass))
                return "level";
            if (!Classes.Contains(newClass))
                return "class";
            if (client.Inventory.Items.All(i => i.TypeID != RequiredItem(life)))
                return "no_item";
            return null;
        }

        // Starts the character's life-th rebirth in the new class, taking the item it costs;
        // null when done, or why not
        public static async Task<string> RebirthAsync(Client client, byte life, byte newClass, string npc)
        {
            var character = client.Character;
            if (character == null)
                return "not_found";
            byte from = character.CurrentClass, level = character.Level;
            string reason = Unmet(client, life, newClass);
            var item = reason == null ? client.Inventory.Items.FirstOrDefault(i => i.TypeID == RequiredItem(life)) : null;
            if (reason == null && (item == null || !client.Inventory.Remove(item)))
                reason = "no_item";
            if (reason != null)
            {
                LogEvents.For(typeof(Rebirth), LogEvents.CharacterRebirthRejected).Information(
                    "{Name} can't be reborn (rebirth {Life}) from class {FromClass} into {Class} at NPC {Npc}: {Reason}",
                    character.Name, life, from, newClass, npc, reason);
                return reason;
            }
            await ItemsRepository.DeleteAsync(item.ItemID);
            await client.SendAsync(MsgItem.Removed(item.ItemID));

            if (life == 1)
                character.FirstLifeLevel = level;
            else
            {
                character.SecondLifeLevel = level;
                character.AncestorClass = character.PreviousClass;
            }
            character.PreviousClass = from;
            character.Rebirths = life;
            character.CurrentClass = newClass;
            character.Level = NewLevel;
            character.Experience = 0;
            Attributes.SetStarting(character);
            character.AttributePoints = (ushort)(LifePoints(1, life == 1 ? from : character.AncestorClass, character.FirstLifeLevel) +
                (life == 2 ? LifePoints(2, from, level) : 0));

            await ChangeSpellsAsync(client, life, from, newClass);
            await LowerGearAsync(client);
            character.HealthPoints = Attributes.MaxHealth(client);
            character.ManaPoints = Attributes.MaxMana(client);

            await client.SendAsync(new MsgUserAttrib(client.ID)
                .Append(AttributeType.Rebirths, character.Rebirths)
                .Append(AttributeType.Class, character.CurrentClass)
                .Append(AttributeType.Level, character.Level)
                .Append(AttributeType.Experience, character.Experience)
                .Append(AttributeType.Strength, character.Strength)
                .Append(AttributeType.Agility, character.Agility)
                .Append(AttributeType.Vitality, character.Vitality)
                .Append(AttributeType.Spirit, character.Spirit)
                .Append(AttributeType.AttributePoints, character.AttributePoints)
                .Append(AttributeType.MaxHealth, character.HealthPoints)
                .Append(AttributeType.Health, character.HealthPoints)
                .Append(AttributeType.MaxMana, character.ManaPoints)
                .Append(AttributeType.Mana, character.ManaPoints));
            await Movement.SendToViewAsync(client, new MsgUserAttrib(client.ID)
                .Append(AttributeType.Rebirths, character.Rebirths)
                .Append(AttributeType.Class, character.CurrentClass)
                .Append(AttributeType.Level, character.Level));
            await Equipping.ShowLookAsync(client);
            await Progress.ShowLevelUpAsync(client);

            LogEvents.For(typeof(Rebirth), LogEvents.CharacterReborn).Information(
                "{Name} was reborn (rebirth {Life}) at level {FromLevel} from class {FromClass} into {Class} " +
                "with {Points} attribute points at NPC {Npc} for item {ItemTypeId}",
                character.Name, life, level, from, newClass, character.AttributePoints, npc, item.TypeID);
            await client.SaveAsync("rebirth");
            return null;
        }

        private static async Task ChangeSpellsAsync(Client client, byte life, byte from, byte to)
        {
            foreach (var magic in Kernel.MagicOps.Of(life, from, to, Operation.Forget))
                await Casting.ForgetAsync(client, magic, "rebirth");
            foreach (var magic in Kernel.MagicOps.Of(life, from, to, Operation.Restart))
                if (client.Skills.TryGetSpell(magic, out var spell))
                    await Casting.SetAsync(client, magic,
                        new SkillLevel(0, 0, Math.Max(spell.Level, spell.OldLevel)), spell.Level, "rebirth");
            foreach (var magic in Kernel.MagicOps.Of(life, from, to, Operation.Learn))
                if (!client.Skills.TryGetSpell(magic, out _))
                    await Casting.LearnAsync(client, magic, 0, "rebirth");
        }

        // Worn gear goes back to its lowest level, its wear in proportion; what the new class
        // can't wear is taken off while the inventory has room
        private static async Task LowerGearAsync(Client client)
        {
            foreach (var item in client.Equipment.Items.ToList())
            {
                if (Upgrading.LowestLevel(item.TypeID) is not ItemType lowest)
                    continue;
                item.Amount = item.AmountLimit == 0 ? lowest.Amount
                    : (ushort)Math.Min(lowest.AmountLimit, (long)item.Amount * lowest.AmountLimit / item.AmountLimit);
                item.AmountLimit = lowest.AmountLimit;
                item.TypeID = lowest.ID;
                await ItemsRepository.SetUpgradeAsync(item);
                await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Update));
            }

            foreach (var item in client.Equipment.Items.ToList())
                if (Kernel.ItemTypes.TryGet(item.TypeID, out var type) && type.RequiredProfession != 0 &&
                    !Requirements.ProfessionFits(client.Character.CurrentClass, type.RequiredProfession))
                    await Equipping.UnequipAsync(client, item.ItemID, (ItemPosition)item.Position);
        }

        // A reborn player of level 70 or more may put every attribute point back to allot again
        public static string ReallotUnmet(Database.Models.DbCharacter character) =>
            character.Rebirths == 0 ? "not_reborn" : character.Level < 70 ? "level" : null;

        public static async Task<string> ReallotAsync(Client client, string npc)
        {
            var character = client.Character;
            string reason = ReallotUnmet(character);
            if (reason != null)
            {
                LogEvents.For(typeof(Rebirth), LogEvents.CharacterAllotRejected).Information(
                    "{Name} can't reallot attribute points at NPC {Npc}: {Reason}", character.Name, npc, reason);
                return reason;
            }

            await Progress.RestartAttributesAsync(client, "reallot");
            LogEvents.For(typeof(Rebirth), LogEvents.CharacterReallotted).Information(
                "{Name} put {Points} attribute points back to allot at NPC {Npc}",
                character.Name, character.AttributePoints, npc);
            await client.SaveAsync("reallot");
            return null;
        }
    }
}
