namespace Comet.Game.States
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Packets;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgUserAttrib;

    // Experience, level, attribute points and silver. Each runs on the player's own packet
    // queue, so a character is only ever changed by one of these at a time.
    public static class Progress
    {
        public static async Task AwardExperienceAsync(Client client, ulong amount, string reason)
        {
            var character = client.Character;
            if (character == null || amount == 0)
                return;

            byte from = character.Level;
            var (level, experience) = Kernel.Experience.Gain(character.Level, character.Experience, amount);
            character.Experience = experience;
            if (level == from)
            {
                await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Experience, experience));
                return;
            }

            character.Level = level;
            character.AttributePoints = AddPoints(character.AttributePoints, (level - from) * Attributes.PointsPerLevel);
            await LevelChangedAsync(client, from, reason);
        }

        public static async Task<bool> SetLevelAsync(Client client, byte level, string reason)
        {
            var character = client.Character;
            if (character == null || level < 1 || level > Kernel.Experience.MaxLevel)
                return false;

            byte from = character.Level;
            character.Level = level;
            character.Experience = 0;
            if (level > from)
                character.AttributePoints = AddPoints(character.AttributePoints, (level - from) * Attributes.PointsPerLevel);
            await LevelChangedAsync(client, from, reason);
            return true;
        }

        // A trade changes both players' silver from one player's packet queue, so the
        // balance only changes under the character's lock
        public static async Task<bool> ChangeSilverAsync(Client client, long amount, string reason)
        {
            var character = client.Character;
            if (character == null)
                return false;
            uint silver;
            lock (character)
            {
                long balance = character.Silver + amount;
                if (balance < 0 || balance > uint.MaxValue)
                    return false;
                silver = character.Silver = (uint)balance;
            }

            await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Silver, silver));
            LogEvents.For(typeof(Progress), LogEvents.CharacterSilver).Information(
                "{Name} silver {Amount:+#;-#;0} to {Balance} ({Reason})",
                character.Name, amount, silver, reason);
            return true;
        }

        public static async Task AllotAsync(Client client, byte strength, byte agility, byte vitality, byte spirit)
        {
            var character = client.Character;
            if (character == null)
                return;

            int total = strength + agility + vitality + spirit;
            if (total == 0 || total > character.AttributePoints)
            {
                LogEvents.For(typeof(Progress), LogEvents.CharacterAllotRejected).Warning(
                    "{Name} tried to add {Points} attribute points with {Available} available",
                    character.Name, total, character.AttributePoints);
                await SendAttributesAsync(client);
                return;
            }

            character.Strength = AddPoints(character.Strength, strength);
            character.Agility = AddPoints(character.Agility, agility);
            character.Vitality = AddPoints(character.Vitality, vitality);
            character.Spirit = AddPoints(character.Spirit, spirit);
            character.AttributePoints -= (ushort)total;
            await SendAttributesAsync(client);
            LogEvents.For(typeof(Progress), LogEvents.CharacterAllotted).Debug(
                "{Name} added {Strength}/{Agility}/{Vitality}/{Spirit} to strength/agility/" +
                "vitality/spirit; {Available} points left",
                character.Name, strength, agility, vitality, spirit, character.AttributePoints);
        }

        private static async Task LevelChangedAsync(Client client, byte from, string reason)
        {
            var character = client.Character;
            character.HealthPoints = Attributes.MaxHealth(character);
            character.ManaPoints = Attributes.MaxMana(character);
            await client.SendAsync(new MsgUserAttrib(client.ID)
                .Append(AttributeType.Level, character.Level)
                .Append(AttributeType.Experience, character.Experience)
                .Append(AttributeType.AttributePoints, character.AttributePoints)
                .Append(AttributeType.MaxHealth, character.HealthPoints)
                .Append(AttributeType.Health, character.HealthPoints)
                .Append(AttributeType.MaxMana, character.ManaPoints)
                .Append(AttributeType.Mana, character.ManaPoints));

            if (character.Level > from)
            {
                var effect = MsgAction.LevelUp(client.ID);
                await client.SendAsync(effect);
                await Movement.SendToViewAsync(client, effect);
            }

            LogEvents.For(typeof(Progress), LogEvents.CharacterLevel).Information(
                "{Name} went from level {FromLevel} to {Level} ({Reason})",
                character.Name, from, character.Level, reason);
            await client.SaveAsync("level");
        }

        private static Task SendAttributesAsync(Client client)
        {
            var character = client.Character;
            return client.SendAsync(new MsgUserAttrib(client.ID)
                .Append(AttributeType.Strength, character.Strength)
                .Append(AttributeType.Agility, character.Agility)
                .Append(AttributeType.Vitality, character.Vitality)
                .Append(AttributeType.Spirit, character.Spirit)
                .Append(AttributeType.AttributePoints, character.AttributePoints)
                .Append(AttributeType.MaxHealth, Attributes.MaxHealth(character))
                .Append(AttributeType.MaxMana, Attributes.MaxMana(character)));
        }

        private static ushort AddPoints(ushort value, int added) =>
            (ushort)Math.Min(value + added, ushort.MaxValue);
    }
}
