namespace Comet.Web.Auth;

using System.Security.Cryptography;
using System.Text;
using Comet.Account.Database.Models;
using Comet.Account.Database.Repositories;
using Comet.Network.Security;

public static class Passwords
{
    // Checked against an account that doesn't exist too, so the answer takes as long
    private static readonly byte[] DummySalt = Srp6.CreateSalt();

    // The stored verifier is g^x for the password the game client logs in with, so the web
    // recomputes it from the salt and compares.
    public static bool Matches(DbAccount account, string password)
    {
        if (password == null || password.Length > AccountRules.MaxPasswordLength)
            return false;
        byte[] bytes = Encoding.Latin1.GetBytes(password);
        if (account == null || string.IsNullOrEmpty(account.SrpSalt) ||
            string.IsNullOrEmpty(account.SrpVerifier))
        {
            Srp6.ComputeVerifier(DummySalt, bytes);
            return false;
        }

        byte[] computed = Srp6.ComputeVerifier(Convert.FromHexString(account.SrpSalt), bytes);
        return CryptographicOperations.FixedTimeEquals(
            computed, Convert.FromHexString(account.SrpVerifier));
    }
}
