namespace Comet.Account.Database.Repositories
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Account.Database.Models;
    using Comet.Core.Logging;
    using Comet.Network.Security;
    using Microsoft.EntityFrameworkCore;
    using MySqlConnector;

    /// <summary>
    /// Repository for defining data access layer (DAL) logic for the account table. Allows
    /// the server to fetch account details for player authentication. Accounts are fetched
    /// on demand for each player authentication request.
    /// </summary>
    public static class AccountsRepository
    {
        /// <summary>
        /// Fetches an account record from the database using the player's username as a
        /// unique key for selecting a single record.
        /// </summary>
        /// <param name="username">Username to pull account info for</param>
        /// <returns>Returns account details from the database.</returns>
        public static async Task<DbAccount> FindAsync(string username)
        {
            using var db = new ServerDbContext();
            return await db.Accounts.Include(x => x.Authority)
                .Include(x => x.Status)
                .Where(x => x.Username == username)
                .SingleOrDefaultAsync();
        }

        /// <summary>
        /// Creates an account with an email address as its name. The name is stored in
        /// lower case; logins match it regardless of case.
        /// </summary>
        /// <param name="username">Email address used as the account name</param>
        /// <param name="password">Password, as it will be typed in the client</param>
        /// <returns>
        /// The new account, or null and the reason it wasn't created
        /// (<c>invalid_username</c>, <c>invalid_password</c>, <c>username_taken</c>).
        /// </returns>
        public static async Task<(DbAccount Account, string Error)> CreateAsync(
            string username, string password)
        {
            username = username?.Trim().ToLowerInvariant();
            string error = AccountRules.CheckUsername(username)
                ?? AccountRules.CheckPassword(password);
            if (error == null)
            {
                using var db = new ServerDbContext();
                if (await db.Accounts.AnyAsync(x => x.Username == username))
                    error = "username_taken";
                else
                {
                    var account = new DbAccount
                    {
                        Username = username,
                        AuthorityID = 1,
                        StatusID = 1,
                        Registered = DateTime.UtcNow
                    };
                    SetPassword(account, password);
                    db.Accounts.Add(account);
                    try
                    {
                        await db.SaveChangesAsync();
                        LogEvents.For(typeof(AccountsRepository), LogEvents.AccountCreated)
                            .Information("Account {Username} created (account {AccountId})",
                                username, account.AccountID);
                        return (account, null);
                    }
                    catch (DbUpdateException e) when (IsDuplicateKey(e))
                    {
                        // Another request created the same name since the check above
                        error = "username_taken";
                    }
                }
            }

            LogEvents.For(typeof(AccountsRepository), LogEvents.AccountCreateFailed)
                .Warning("Account {Username} not created: {Reason}", username, error);
            return (null, error);
        }

        /// <summary>
        /// Sets an account's password for the SRP6 login: stores a new random salt and the
        /// verifier computed from the password. The password itself isn't stored.
        /// </summary>
        /// <param name="username">Account name (email address)</param>
        /// <param name="password">New password, as it will be typed in the client</param>
        /// <returns>
        /// Null on success, or <c>unknown_account</c> or <c>invalid_password</c>.
        /// </returns>
        public static async Task<string> SetPasswordAsync(string username, string password)
        {
            if (AccountRules.CheckPassword(password) is string error)
                return error;

            using var db = new ServerDbContext();
            var account = await db.Accounts.SingleOrDefaultAsync(x => x.Username == username);
            if (account == null)
                return "unknown_account";

            SetPassword(account, password);
            await db.SaveChangesAsync();
            return null;
        }

        /// <summary>Stores a new salt and verifier for a password on the account.</summary>
        /// <param name="account">Account to update</param>
        /// <param name="password">New password, as it will be typed in the client</param>
        public static void SetPassword(DbAccount account, string password)
        {
            var salt = Srp6.CreateSalt();
            account.SrpSalt = Convert.ToHexString(salt);
            account.SrpVerifier = Convert.ToHexString(
                Srp6.ComputeVerifier(salt, Encoding.Latin1.GetBytes(password)));
        }

        /// <summary>
        /// Starts an SRP6 login for the account, or returns null if the account has no
        /// password set (no SRP6 salt and verifier).
        /// </summary>
        /// <param name="account">Account logging in</param>
        public static Srp6 BeginLogin(DbAccount account)
        {
            if (string.IsNullOrEmpty(account.SrpSalt) || string.IsNullOrEmpty(account.SrpVerifier))
                return null;
            return new Srp6(
                Convert.FromHexString(account.SrpSalt),
                Convert.FromHexString(account.SrpVerifier));
        }

        /// <summary>True if saving failed because a unique key already holds the value.</summary>
        internal static bool IsDuplicateKey(DbUpdateException e) =>
            e.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };
    } 
}
