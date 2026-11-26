namespace Fetih.Account.Database.Repositories
{
    using System.Linq;

    // The client's login box takes up to 127 bytes, so account names are email addresses.
    public static class AccountRules
    {
        public const int MaxUsernameLength = 127;
        public const int MinPasswordLength = 6;
        public const int MaxPasswordLength = 16;

        public static string CheckUsername(string username)
        {
            if (string.IsNullOrEmpty(username) || username.Length > MaxUsernameLength)
                return "invalid_username";

            // Printable ASCII without spaces, one '@', and a dot in the domain. The
            // client sends the name as typed, so anything outside ASCII can't match.
            if (!username.All(c => c > ' ' && c < 0x7F))
                return "invalid_username";
            int at = username.IndexOf('@');
            if (at <= 0 || at != username.LastIndexOf('@'))
                return "invalid_username";
            string domain = username[(at + 1)..];
            int dot = domain.LastIndexOf('.');
            if (dot <= 0 || dot == domain.Length - 1 || domain.Contains(".."))
                return "invalid_username";
            return null;
        }

        // The client's password box takes at most 16 characters.
        public static string CheckPassword(string password)
        {
            if (password == null || password.Length < MinPasswordLength ||
                password.Length > MaxPasswordLength)
                return "invalid_password";
            if (!password.All(c => c >= ' ' && c < 0x7F))
                return "invalid_password";
            return null;
        }

        public static string Explain(string reason) => reason switch
        {
            "invalid_username" => "The account name must be an email address of at most " +
                $"{MaxUsernameLength} characters, like player@example.com.",
            "invalid_password" => $"The password must be {MinPasswordLength} to " +
                $"{MaxPasswordLength} characters long, using English letters, digits, " +
                "spaces and symbols.",
            "username_taken" => "An account with this email address already exists " +
                "(capital letters don't make a different address).",
            "unknown_account" => "There is no account with this email address.",
            _ => reason
        };
    }
}
