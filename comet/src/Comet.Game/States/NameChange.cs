namespace Comet.Game.States
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Arena;
    using Comet.Game.Chat;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Flowers;
    using Comet.Game.Nobility;
    using Comet.Game.Packets;
    using Comet.Game.Social;
    using Microsoft.EntityFrameworkCore;
    using static Comet.Game.Packets.MsgChangeName;

    // One-time character rename for 810 CPs, through NameRegistrar's change-name window
    // (MsgChangeName 2080). The 5619 client doesn't rename its own hero live on the reply, so
    // the new name takes effect on the player's side after relogging while the database and
    // server caches switch immediately.
    public static class NameChange
    {
        public const uint CostCPs = 810;

        public static Task QueryAsync(Client client)
        {
            var character = client.Character;
            if (character == null)
                return Task.CompletedTask;
            ushort used = character.NameChanged ? (ushort)1 : (ushort)0;
            ushort left = character.NameChanged ? (ushort)0 : (ushort)1;
            return client.SendAsync(MsgChangeName.QueryReply(used, left));
        }

        public static async Task<string> ChangeAsync(Client client, string rawName)
        {
            var character = client.Character;
            if (character == null)
                return "not_found";

            string newName = rawName?.Trim() ?? string.Empty;
            if (character.NameChanged)
            {
                Reject(character.Name, newName, "already_used");
                await Talk.NoticeAsync(client, "You can't change your name more than once!");
                await client.SendAsync(MsgChangeName.Close());
                return "already_used";
            }

            if (CharacterRules.CheckName(newName) is string nameError)
            {
                Reject(character.Name, newName, nameError);
                await Talk.NoticeAsync(client, "Invalid name. Use 4 to 15 ASCII characters without spaces.");
                return nameError;
            }

            if (string.Equals(newName, character.Name, StringComparison.OrdinalIgnoreCase))
            {
                Reject(character.Name, newName, "same_name");
                await Talk.NoticeAsync(client, "Your name has not been changed!");
                return "same_name";
            }

            if (character.Jewels < CostCPs)
            {
                Reject(character.Name, newName, "no_cps");
                await Talk.NoticeAsync(client, $"You don't have the {CostCPs} CPs to change your name!");
                return "no_cps";
            }

            if (await CharactersRepository.ExistsAsync(newName))
            {
                Reject(character.Name, newName, "name_taken");
                await client.SendAsync(MsgChangeName.Duplicate());
                return "name_taken";
            }

            string oldName = character.Name;
            uint jewelsAfter = character.Jewels - CostCPs;
            try
            {
                await CharactersRepository.RenameAsync(character.CharacterID, oldName, newName, jewelsAfter);
            }
            catch (DbUpdateException e) when (CharactersRepository.IsDuplicateName(e))
            {
                Reject(oldName, newName, "name_taken");
                await client.SendAsync(MsgChangeName.Duplicate());
                return "name_taken";
            }

            await Progress.ChangeConquerPointsAsync(client, -CostCPs, "name_change");
            character.Rename(newName);
            character.NameChanged = true;
            Kernel.Players.Rename(client.ID, oldName, newName, client);
            await UpdateCachesAsync(client, oldName, newName);

            await client.SendAsync(MsgChangeName.Success());
            await Talk.NoticeAsync(client,
                $"Your name has been changed to {newName}. Please log out and back in to see your new name.");

            LogEvents.For(typeof(NameChange), LogEvents.CharacterRenamed).Information(
                "{OldName} changed name to {Name} (character {CharacterId}, cost {Cost} CPs)",
                oldName, newName, character.CharacterID, CostCPs);
            return null;
        }

        private static async Task UpdateCachesAsync(Client client, string oldName, string newName)
        {
            foreach (var other in Kernel.Players.Values)
            {
                if (other.Character == null || other == client)
                    continue;
                if (string.Equals(other.Character.Spouse, oldName, StringComparison.OrdinalIgnoreCase))
                    other.Character.Spouse = newName;
                if (other.Friends != null && other.Friends.ContainsKey(client.ID))
                    other.Friends[client.ID] = newName;
                if (other.Enemies != null && other.Enemies.TryGetValue(client.ID, out var enemy))
                    other.Enemies[client.ID] = (newName, enemy.Mesh);
                if (other.TradePartners != null && other.TradePartners.TryGetValue(client.ID, out var partner))
                    other.TradePartners[client.ID] = partner with { Name = newName };
            }

            await Guilds.RenameAsync(client.ID, newName);
            await Clans.RenameAsync(client.ID, newName);
            Peerage.Rename(client.ID, newName);
            FlowerRanking.Rename(client.ID, newName);
            Qualifier.Rename(client.ID, newName);
        }

        private static void Reject(string name, string requested, string reason) =>
            LogEvents.For(typeof(NameChange), LogEvents.CharacterRenameRejected).Information(
                "{Name} can't change name to {Requested}: {Reason}", name, requested, reason);
    }
}
