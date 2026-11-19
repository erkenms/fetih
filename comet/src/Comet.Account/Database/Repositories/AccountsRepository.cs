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

    public static class AccountsRepository
    {
        public static async Task<DbAccount> FindAsync(string username)
        {
            using var db = new ServerDbContext();
            return await db.Accounts.Include(x => x.Authority)
                .Include(x => x.Status)
                .Where(x => x.Username == username)
                .SingleOrDefaultAsync();
        }

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

        public static void SetPassword(DbAccount account, string password)
        {
            var salt = Srp6.CreateSalt();
            account.SrpSalt = Convert.ToHexString(salt);
            account.SrpVerifier = Convert.ToHexString(
                Srp6.ComputeVerifier(salt, Encoding.Latin1.GetBytes(password)));
        }

        public static Srp6 BeginLogin(DbAccount account)
        {
            if (string.IsNullOrEmpty(account.SrpSalt) || string.IsNullOrEmpty(account.SrpVerifier))
                return null;
            return new Srp6(
                Convert.FromHexString(account.SrpSalt),
                Convert.FromHexString(account.SrpVerifier));
        }

        internal static bool IsDuplicateKey(DbUpdateException e) =>
            e.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };
    } 
}
