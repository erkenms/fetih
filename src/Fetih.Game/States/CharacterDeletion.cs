namespace Fetih.Game.States
{
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Chat;
    using Fetih.Game.Database.Repositories;
    using Fetih.Game.Social;
    using Fetih.Network.Sockets;

    // Options -> Delete sends MsgAction 95 with the warehouse (secondary) password in Command
    // (0 when none is set). Not echoed; disconnecting returns the client to login.
    public static class CharacterDeletion
    {
        public static async Task DeleteAsync(Client client, uint password)
        {
            var character = client.Character;
            if (character == null || client.Deleted)
                return;

            if (password != character.SecondPassword)
            {
                Reject(client, "wrong_password");
                await Talk.NoticeAsync(client, "Incorrect secondary password.");
                return;
            }
            if (Guilds.Of(client.ID) != null)
            {
                Reject(client, "in_guild");
                await Talk.NoticeAsync(client, "Please leave your guild before deleting your character.");
                return;
            }
            if (Clans.Of(client.ID) != null)
            {
                Reject(client, "in_clan");
                await Talk.NoticeAsync(client, "Please leave your clan before deleting your character.");
                return;
            }

            if (Marriage.IsMarried(character) &&
                Kernel.Players.TryGet(Marriage.SpouseOf(character), out var spouse) &&
                spouse.Character != null)
                spouse.Character.Spouse = Marriage.None;

            client.Deleted = true;
            await CharactersRepository.DeleteAsync(character.CharacterID, character.Name, Marriage.None);
            LogEvents.For(typeof(CharacterDeletion), LogEvents.CharacterDeleted).Information(
                "{Name} deleted character {CharacterId} (account {AccountId})",
                character.Name, character.CharacterID, character.AccountID);
            client.Disconnect(DisconnectReasons.Deleted);
        }

        private static void Reject(Client client, string reason) =>
            LogEvents.For(typeof(CharacterDeletion), LogEvents.CharacterDeleteRejected).Warning(
                "{Name} couldn't delete character {CharacterId}: {Reason}",
                client.Character?.Name, client.ID, reason);
    }
}
