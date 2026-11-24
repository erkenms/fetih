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
        public const byte MaxRebirths = 2, NewLevel = 15, WaterSaint = 135, ReincarnationLevel = 110;
        public const uint CelestialStone = 721259, ExemptionToken = 723701, OblivionDew = 711083;

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
            if (reason == null && (item == null || !client.Inventory.TakeOne(item)))
                reason = "no_item";
            if (reason != null)
            {
                LogEvents.For(typeof(Rebirth), LogEvents.CharacterRebirthRejected).Information(
                    "{Name} can't be reborn (rebirth {Life}) from class {FromClass} into {Class} at NPC {Npc}: {Reason}",
                    character.Name, life, from, newClass, npc, reason);
                return reason;
            }
            await ItemActions.SaveTakenAsync(client, item);

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

        // Reincarnation (MsgMeteSpecial 1066): a 2nd-reborn master at level 110+ spends an
        // Oblivion Dew to shift their 3-life class history (dropping AncestorClass) and start
        // at level 15 in the chosen class without gaining bonus rebirths/BP.
        public static async Task<string> ReincarnateAsync(Client client, byte newClass, int body)
        {
            var character = client.Character;
            if (character == null)
                return "not_found";

            byte from = character.CurrentClass, level = character.Level;
            if (client.Life.Dead)
                return await RejectReincarnateAsync(client, newClass, "dead", MsgMeteSpecial.Result.Dead);
            if (character.Rebirths < MaxRebirths)
                return await RejectReincarnateAsync(client, newClass, "not_second_reborn", MsgMeteSpecial.Result.NotSecondReborn);
            if (Promotion.Rank(from) < Promotion.MaxRank)
                return await RejectReincarnateAsync(client, newClass, "promotion", MsgMeteSpecial.Result.NotMaxRank);
            if (level < ReincarnationLevel)
                return await RejectReincarnateAsync(client, newClass, "level", MsgMeteSpecial.Result.LevelLow);
            if (!Classes.Contains(newClass))
                return await RejectReincarnateAsync(client, newClass, "class", null);

            var dew = client.Inventory.Items.FirstOrDefault(i => i.TypeID == OblivionDew);
            if (dew == null || !client.Inventory.Remove(dew))
                return await RejectReincarnateAsync(client, newClass, "no_item", MsgMeteSpecial.Result.NoItem);

            await ItemsRepository.DeleteAsync(dew.ItemID);
            await client.SendAsync(MsgItem.Removed(dew.ItemID));

            byte dropped = character.AncestorClass;
            character.AncestorClass = character.PreviousClass;
            character.PreviousClass = from;
            character.CurrentClass = newClass;
            character.SecondLifeLevel = level;
            character.Level = NewLevel;
            character.Experience = 0;

            uint? newMesh = BodyMesh(character.Mesh, body);
            if (newMesh is uint mesh)
                character.Mesh = mesh;

            Attributes.SetStarting(character);
            character.AttributePoints = (ushort)(LifePoints(1, character.AncestorClass, character.FirstLifeLevel) +
                LifePoints(2, from, level));

            await ChangeSpellsAsync(client, MaxRebirths, from, newClass);
            await ForgetDroppedLifeAsync(client, dropped, from, newClass);
            await LowerGearAsync(client);
            character.HealthPoints = Attributes.MaxHealth(client);
            character.ManaPoints = Attributes.MaxMana(client);

            var self = new MsgUserAttrib(client.ID)
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
                .Append(AttributeType.Mana, character.ManaPoints);
            var view = new MsgUserAttrib(client.ID)
                .Append(AttributeType.Class, character.CurrentClass)
                .Append(AttributeType.Level, character.Level);
            if (newMesh.HasValue)
            {
                uint visibleMesh = client.Life.Mesh(character);
                self.Append(AttributeType.Mesh, visibleMesh);
                view.Append(AttributeType.Mesh, visibleMesh);
            }
            await client.SendAsync(self);
            await Movement.SendToViewAsync(client, view);
            await Equipping.ShowLookAsync(client);
            await Progress.ShowLevelUpAsync(client);
            await client.SendAsync(new MsgMeteSpecial(MsgMeteSpecial.Result.Success));

            LogEvents.For(typeof(Rebirth), LogEvents.CharacterReincarnated).Information(
                "{Name} reincarnated at level {FromLevel} from {FromClass} into {Class} " +
                "(lives {Ancestor}/{Previous}/{Current}, {Points} points)",
                character.Name, level, from, newClass, character.AncestorClass, character.PreviousClass,
                character.CurrentClass, character.AttributePoints);
            await client.SaveAsync("reincarnation");
            return null;
        }

        private static async Task<string> RejectReincarnateAsync(Client client, byte newClass, string reason,
            MsgMeteSpecial.Result? reply)
        {
            LogEvents.For(typeof(Rebirth), LogEvents.CharacterReincarnateRejected).Information(
                "{Name} can't reincarnate from class {FromClass} into {Class}: {Reason}",
                client.Character?.Name, client.Character?.CurrentClass, newClass, reason);
            if (reply is MsgMeteSpecial.Result code)
                await client.SendAsync(new MsgMeteSpecial(code));
            return reason;
        }

        // The client sends body 1/2 for female (agile/muscular) and 3/4 for male (agile/muscular);
        // keep the player's gender and only apply a matching body.
        private static uint? BodyMesh(uint currentMesh, int body)
        {
            bool female = currentMesh is (uint)BodyType.AgileFemale or (uint)BodyType.MuscularFemale;
            return (female, body) switch
            {
                (true, 1) => (uint)BodyType.AgileFemale,
                (true, 2) => (uint)BodyType.MuscularFemale,
                (false, 3) => (uint)BodyType.AgileMale,
                (false, 4) => (uint)BodyType.MuscularMale,
                _ => null,
            };
        }

        private static byte BaseClass(byte cls)
        {
            int line = cls / 10;
            return line switch
            {
                13 => 132,
                14 => 142,
                _ when line > 0 => (byte)(line * 10 + 1),
                _ => 0,
            };
        }

        private static async Task ForgetDroppedLifeAsync(Client client, byte dropped, byte from, byte to)
        {
            byte droppedBase = BaseClass(dropped);
            if (droppedBase == 0)
                return;
            var kept = Kernel.MagicOps.Of(MaxRebirths, from, to, Operation.Restart)
                .Concat(Kernel.MagicOps.Of(MaxRebirths, from, to, Operation.Learn))
                .ToHashSet();
            foreach (var magic in Kernel.MagicOps.Of(0, 0, droppedBase, Operation.FirstLife))
                if (!kept.Contains(magic) && client.Skills.TryGetSpell(magic, out _))
                    await Casting.ForgetAsync(client, magic, "reincarnation");
            foreach (var magic in Kernel.MagicOps.Of(0, droppedBase, droppedBase, Operation.Pure))
                if (!kept.Contains(magic) && client.Skills.TryGetSpell(magic, out _))
                    await Casting.ForgetAsync(client, magic, "reincarnation");
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

            // A reborn class starts again below the rank that holds a second weapon
            var left = client.Equipment[ItemPosition.LeftHand];
            if (left != null && Equipping.LeftHandUnmet(
                    client, left.TypeID, client.Equipment.TypeAt(ItemPosition.RightHand)) != null)
                await Equipping.UnequipAsync(client, left.ItemID, ItemPosition.LeftHand);
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
