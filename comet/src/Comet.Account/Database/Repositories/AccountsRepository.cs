namespace Comet.Account.Database.Repositories
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Account.Database.Models;
    using Comet.Network.Security;
    using Microsoft.EntityFrameworkCore;

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
        /// Sets an account's password for the SRP6 login: stores a new random salt and the
        /// verifier computed from the password. The password itself isn't stored.
        /// </summary>
        /// <param name="username">Username of the account</param>
        /// <param name="password">New password, as it will be typed in the client</param>
        /// <returns>Returns false if the account doesn't exist.</returns>
        public static async Task<bool> SetPasswordAsync(string username, string password)
        {
            using var db = new ServerDbContext();
            var account = await db.Accounts.SingleOrDefaultAsync(x => x.Username == username);
            if (account == null)
                return false;

            SetPassword(account, password);
            await db.SaveChangesAsync();
            return true;
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
        /// Starts an SRP6 login for the account, or returns null if no password has been
        /// set for SRP6 yet (accounts from before patch 5619).
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
    } 
}
