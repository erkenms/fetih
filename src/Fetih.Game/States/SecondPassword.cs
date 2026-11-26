namespace Fetih.Game.States
{
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Chat;
    using Fetih.Game.Packets;
    using static Fetih.Game.Packets.Msg2ndPsw;

    // A number the player sets to guard warehouses and unlocking items. Given once a
    // session; three wrong tries and it isn't taken again until the next login.
    public static class SecondPassword
    {
        public const int MaxFailures = 3;

        public static bool IsSet(Client client) => client.Character?.SecondPassword is > 0;

        public static bool IsOpen(Client client) => !IsSet(client) || client.SecondPasswordVerified;

        public static Task LoginAsync(Client client) =>
            IsSet(client) ? client.SendAsync(new Msg2ndPsw(PasswordRequest.Correct, HasPassword)) : Task.CompletedTask;

        // For guarded actions: false, and the client asked for the password, until it's given
        public static async Task<bool> EnsureOpenAsync(Client client, string use)
        {
            if (IsOpen(client))
                return true;
            Reject(client, use, "not_verified");
            await client.SendAsync(new Msg2ndPsw(PasswordRequest.Correct, HasPassword));
            await Talk.NoticeAsync(client, "Please input the secondary password.");
            return false;
        }

        public static Task HandleAsync(Client client, Msg2ndPsw msg)
        {
            if (client.Character == null)
                return Task.CompletedTask;
            switch (msg.Request)
            {
                case PasswordRequest.Verify: return VerifyAsync(client, msg);
                case PasswordRequest.Set: return SetAsync(client, msg);
                default:
                    Reject(client, msg.Request.ToString().ToLowerInvariant(), "unsupported");
                    return Task.CompletedTask;
            }
        }

        // The client has sent the password in either field
        private static async Task VerifyAsync(Client client, Msg2ndPsw msg)
        {
            var character = client.Character;
            if (!IsSet(client) || client.SecondPasswordVerified)
            {
                await client.SendAsync(new Msg2ndPsw(PasswordRequest.Correct, State(client)));
                return;
            }
            if (!await CheckAsync(client, "verify", msg.Password == character.SecondPassword ||
                    msg.NewPassword == character.SecondPassword))
                return;

            client.SecondPasswordVerified = true;
            await client.SendAsync(new Msg2ndPsw(PasswordRequest.Correct, State(client)));
            LogEvents.For(typeof(SecondPassword), LogEvents.SecondPasswordVerified).Information(
                "{Name} gave the second password (character {CharacterId})", character.Name, character.CharacterID);
        }

        // A first password needs nothing else; changing it, or removing it with 0, needs the old one
        private static async Task SetAsync(Client client, Msg2ndPsw msg)
        {
            var character = client.Character;
            if (!IsSet(client) && msg.NewPassword == 0)
            {
                Reject(client, "set", "no_password");
                return;
            }
            if (IsSet(client) && !await CheckAsync(client, "set", msg.Password == character.SecondPassword))
                return;

            character.SecondPassword = msg.NewPassword;
            client.SecondPasswordVerified = msg.NewPassword != 0;
            await client.SaveAsync("second_password");
            await client.SendAsync(new Msg2ndPsw(PasswordRequest.Correct, State(client)));
            await Talk.NoticeAsync(client, msg.NewPassword == 0
                ? "Your secondary password was removed."
                : "Your secondary password was set.");
            LogEvents.For(typeof(SecondPassword), LogEvents.SecondPasswordSet).Information(
                "{Name} {Change} the second password (character {CharacterId})",
                character.Name, msg.NewPassword == 0 ? "removed" : "set", character.CharacterID);
        }

        private static async Task<bool> CheckAsync(Client client, string use, bool correct)
        {
            if (client.SecondPasswordFailures >= MaxFailures)
            {
                Reject(client, use, "too_many_failures");
                await client.SendAsync(new Msg2ndPsw(PasswordRequest.Wrong));
                await Talk.NoticeAsync(client, "Too many wrong secondary passwords. Log in again to retry.");
                return false;
            }
            if (correct)
                return true;
            client.SecondPasswordFailures++;
            Reject(client, use, "wrong_password");
            await client.SendAsync(new Msg2ndPsw(PasswordRequest.Wrong));
            await Talk.NoticeAsync(client, "Incorrect secondary password.");
            return false;
        }

        private static uint State(Client client) =>
            (IsSet(client) ? HasPassword : 0) | (client.SecondPasswordVerified ? Verified : 0);

        private static void Reject(Client client, string use, string reason) =>
            LogEvents.For(typeof(SecondPassword), LogEvents.SecondPasswordRejected).Warning(
                "Second password check for {Name} to {Use} failed: {Reason} ({Failures} wrong tries)",
                client.Character?.Name, use, reason, client.SecondPasswordFailures);
    }
}
