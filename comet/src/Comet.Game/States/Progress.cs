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

        // Conquer points, which the database calls jewels
        public static async Task<bool> ChangeConquerPointsAsync(Client client, long amount, string reason)
        {
            var character = client.Character;
            if (character == null)
                return false;
            uint points;
            lock (character)
            {
                long balance = character.Jewels + amount;
                if (balance < 0 || balance > uint.MaxValue)
                    return false;
                points = character.Jewels = (uint)balance;
            }

            await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Jewels, points));
            LogEvents.For(typeof(Progress), LogEvents.CharacterConquerPoints).Information(
                "{Name} conquer points {Amount:+#;-#;0} to {Balance} ({Reason})",
                character.Name, amount, points, reason);
            return true;
        }

        public static async Task AllotAsync(Client client, uint strength, uint agility, uint vitality, uint spirit)
        {
            var character = client.Character;
            if (character == null)
                return;

            ulong total = (ulong)strength + agility + vitality + spirit;
            if (total == 0 || total > character.AttributePoints)
            {
                LogEvents.For(typeof(Progress), LogEvents.CharacterAllotRejected).Warning(
                    "{Name} tried to add {Points} attribute points with {Available} available",
                    character.Name, total, character.AttributePoints);
                await SendAttributesAsync(client);
                return;
            }

            ushort maxHealth = Attributes.MaxHealth(client), maxMana = Attributes.MaxMana(client);
            character.Strength = AddPoints(character.Strength, (int)strength);
            character.Agility = AddPoints(character.Agility, (int)agility);
            character.Vitality = AddPoints(character.Vitality, (int)vitality);
            character.Spirit = AddPoints(character.Spirit, (int)spirit);
            character.AttributePoints -= (ushort)total;
            await SendAttributesAsync(client);
            await FollowMaximumsAsync(client, maxHealth, maxMana);
            LogEvents.For(typeof(Progress), LogEvents.CharacterAllotted).Debug(
                "{Name} added {Strength}/{Agility}/{Vitality}/{Spirit} to strength/agility/" +
                "vitality/spirit; {Available} points left",
                character.Name, strength, agility, vitality, spirit, character.AttributePoints);
        }

        public enum Stat { Strength, Agility, Vitality, Spirit, Points }

        public static async Task SetStatAsync(Client client, Stat stat, int amount, bool relative, string reason)
        {
            var character = client.Character;
            if (character == null)
                return;
            ushort current = stat switch
            {
                Stat.Strength => character.Strength,
                Stat.Agility => character.Agility,
                Stat.Vitality => character.Vitality,
                Stat.Spirit => character.Spirit,
                _ => character.AttributePoints,
            };
            ushort value = (ushort)Math.Clamp(relative ? current + amount : amount, 0, ushort.MaxValue);
            ushort oldMaxHealth = Attributes.MaxHealth(client), oldMaxMana = Attributes.MaxMana(client);
            switch (stat)
            {
                case Stat.Strength: character.Strength = value; break;
                case Stat.Agility: character.Agility = value; break;
                case Stat.Vitality: character.Vitality = value; break;
                case Stat.Spirit: character.Spirit = value; break;
                default: character.AttributePoints = value; break;
            }

            await SendAttributesAsync(client);
            await FollowMaximumsAsync(client, oldMaxHealth, oldMaxMana);
            LogEvents.For(typeof(Progress), LogEvents.CharacterAllotted).Information(
                "{Name} {Stat} set from {From} to {To} ({Reason})",
                character.Name, stat.ToString(), current, value, reason);
        }

        // Every attribute point back to allot again, over the starting attributes
        public static async Task RestartAttributesAsync(Client client, string reason)
        {
            var character = client.Character;
            ushort oldMaxHealth = Attributes.MaxHealth(client), oldMaxMana = Attributes.MaxMana(client);
            int total = character.Strength + character.Agility + character.Vitality + character.Spirit +
                character.AttributePoints;
            Attributes.SetStarting(character);
            character.AttributePoints = (ushort)Math.Clamp(total - character.Strength - character.Agility -
                character.Vitality - character.Spirit, 0, ushort.MaxValue);
            await SendAttributesAsync(client);
            await FollowMaximumsAsync(client, oldMaxHealth, oldMaxMana);
            LogEvents.For(typeof(Progress), LogEvents.CharacterAllotted).Information(
                "{Name} has {Points} attribute points to allot again ({Reason})",
                character.Name, character.AttributePoints, reason);
        }

        public static async Task SetClassAsync(Client client, byte currentClass)
        {
            var character = client.Character;
            ushort oldMaxHealth = Attributes.MaxHealth(client), oldMaxMana = Attributes.MaxMana(client);
            character.CurrentClass = currentClass;
            var change = new MsgUserAttrib(client.ID, AttributeType.Class, currentClass);
            await client.SendAsync(change);
            await Movement.SendToViewAsync(client, change);
            await SendAttributesAsync(client);
            await FollowMaximumsAsync(client, oldMaxHealth, oldMaxMana);
        }

        private static async Task LevelChangedAsync(Client client, byte from, string reason)
        {
            var character = client.Character;
            character.HealthPoints = Attributes.MaxHealth(client);
            character.ManaPoints = Attributes.MaxMana(client);
            await client.SendAsync(new MsgUserAttrib(client.ID)
                .Append(AttributeType.Level, character.Level)
                .Append(AttributeType.Experience, character.Experience)
                .Append(AttributeType.AttributePoints, character.AttributePoints)
                .Append(AttributeType.MaxHealth, character.HealthPoints)
                .Append(AttributeType.Health, character.HealthPoints)
                .Append(AttributeType.MaxMana, character.ManaPoints)
                .Append(AttributeType.Mana, character.ManaPoints));

            if (character.Level > from)
                await ShowLevelUpAsync(client);

            LogEvents.For(typeof(Progress), LogEvents.CharacterLevel).Information(
                "{Name} went from level {FromLevel} to {Level} ({Reason})",
                character.Name, from, character.Level, reason);
            await client.SaveAsync("level");
        }

        // The level-up halo. MsgAction 92 plays it on the player's own client with the sound
        // and message, but the 5619 client doesn't show it to others in view; they get the
        // 3D effect by name.
        public static async Task ShowLevelUpAsync(Client client)
        {
            await client.SendAsync(MsgAction.LevelUp(client.ID));
            await Movement.SendToViewAsync(client, MsgName.RoleEffectOf(client.ID, LevelUpEffect));
        }

        public const string LevelUpEffect = "LevelUp";

        // Health and mana go up with their maximums, so vitality and spirit points show at
        // once; a lower maximum cuts them. The dead stay at 0 health.
        private static async Task FollowMaximumsAsync(Client client, ushort oldMaxHealth, ushort oldMaxMana)
        {
            var character = client.Character;
            if (!client.Life.Dead)
            {
                ushort health = Follow(character.HealthPoints, oldMaxHealth, Attributes.MaxHealth(client));
                if (health != character.HealthPoints)
                {
                    character.HealthPoints = health;
                    await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Health, health));
                }
            }

            ushort mana = Follow(character.ManaPoints, oldMaxMana, Attributes.MaxMana(client));
            if (mana != character.ManaPoints)
            {
                character.ManaPoints = mana;
                await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Mana, mana));
            }
        }

        private static ushort Follow(ushort value, ushort oldMaximum, ushort maximum) =>
            (ushort)Math.Clamp(value + Math.Max(0, maximum - oldMaximum), 0, maximum);

        private static Task SendAttributesAsync(Client client)
        {
            var character = client.Character;
            return client.SendAsync(new MsgUserAttrib(client.ID)
                .Append(AttributeType.Strength, character.Strength)
                .Append(AttributeType.Agility, character.Agility)
                .Append(AttributeType.Vitality, character.Vitality)
                .Append(AttributeType.Spirit, character.Spirit)
                .Append(AttributeType.AttributePoints, character.AttributePoints)
                .Append(AttributeType.MaxHealth, Attributes.MaxHealth(client))
                .Append(AttributeType.MaxMana, Attributes.MaxMana(client)));
        }

        private static ushort AddPoints(ushort value, int added) =>
            (ushort)Math.Min(value + added, ushort.MaxValue);
    }
}
