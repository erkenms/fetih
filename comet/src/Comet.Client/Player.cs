namespace Comet.Client
{
    using System;
    using System.Threading.Tasks;

    /// <summary>A new character's name, body and class.</summary>
    public sealed record NewCharacter(string Name, ushort Mesh, ushort Profession);

    /// <summary>
    /// A logged-in player on the game server: the full login a real client goes through,
    /// from the account server to standing on the map. Tests and bots start here.
    /// </summary>
    public sealed class Player : IAsyncDisposable
    {
        public GameClient Game { get; }
        public UserInfo User { get; }
        public ActionMessage Spawn { get; }

        private Player(GameClient game, UserInfo user, ActionMessage spawn)
        {
            this.Game = game;
            this.User = user;
            this.Spawn = spawn;
        }

        /// <summary>
        /// Logs in through the account server, connects to the game server, creates the
        /// character if the account has none, and enters the map. Throws if any step is
        /// refused.
        /// </summary>
        public static async Task<Player> LoginAsync(
            string accountHost, int accountPort, string realm, string username,
            string password, NewCharacter character = null)
        {
            LoginResult login;
            await using (var account = await AccountClient.ConnectAsync(accountHost, accountPort))
                login = await account.LoginAsync(username, password, realm);
            if (!login.Accepted)
                throw new InvalidOperationException(
                    $"Account server refused {username} with code {login.RejectCode}");

            var game = await GameClient.ConnectAsync(login.GameHost, login.GamePort);
            try
            {
                var result = await game.LoginAsync(login.Token);
                if (result.NeedsCharacter)
                {
                    if (character == null)
                        throw new InvalidOperationException($"{username} has no character");
                    var answer = await game.CreateCharacterAsync(
                        character.Name, character.Mesh, character.Profession, (uint)login.Token);
                    if (answer != "ANSWER_OK")
                        throw new InvalidOperationException($"Character not created: {answer}");
                    result = await game.LoginAsync(login.Token);
                }

                if (!result.InGame)
                    throw new InvalidOperationException($"Game server refused: {result.Message}");
                var spawn = await game.EnterMapAsync(result.User.Id);
                return new Player(game, result.User, spawn);
            }
            catch
            {
                await game.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync() => this.Game.DisposeAsync();
    }
}
