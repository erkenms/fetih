namespace Fetih.Web.Auth;

using System.Threading.RateLimiting;
using Fetih.Core.Logging;
using Fetih.Game.States;
using Fetih.Web.Accounts;
using Microsoft.AspNetCore.Authentication;

// Signing in and out, for /giris. Failed attempts count per IP address, and
// Web:LoginAttemptsPerMinute of them lock that address out for the rest of the minute.
public sealed class SignIn(AccountStore accounts, WebConfiguration config)
{
    private readonly PartitionedRateLimiter<string> failures = PartitionedRateLimiter.Create<string, string>(
        ip => RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.Web.LoginAttemptsPerMinute,
            Window = TimeSpan.FromMinutes(1),
        }));

    // Null when signed in, else why not: too_many_attempts, invalid_credentials, banned,
    // locked or staff_only
    public async Task<string?> TryAsync(HttpContext http, string? username, string? password)
    {
        string ip = http.Connection.RemoteIpAddress?.ToString() ?? "";
        using (var check = this.failures.AttemptAcquire(ip, 0))
            if (!check.IsAcquired)
                return "too_many_attempts";

        var account = await accounts.FindAsync(username ?? "");
        if (!Passwords.Matches(account, password))
        {
            this.failures.AttemptAcquire(ip).Dispose();
            LogEvents.For<SignIn>(WebEvents.LoginFailed).Warning(
                "Web login failed for {Username} from {IPAddress}", username, ip);
            return "invalid_credentials";
        }

        string? refusal = AccountStore.LoginRefusal(account) ??
            (config.Web.StaffOnly && (Authority)account.AuthorityID < Authority.Moderator ? "staff_only" : null);
        if (refusal != null)
        {
            LogEvents.For<SignIn>(WebEvents.LoginFailed).Warning(
                "Web login refused for account {AccountId} from {IPAddress}: {Reason}",
                account.AccountID, ip, refusal);
            return refusal;
        }

        var user = WebUser.Create(account);
        await http.SignInAsync(AuthSetup.Scheme, user);
        http.User = user;
        LogEvents.For<SignIn>(WebEvents.Login).Information(
            "Web login by account {AccountId} from {IPAddress}", account.AccountID, ip);
        return null;
    }

    public static async Task SignOutAsync(HttpContext http)
    {
        if (http.User.Identity?.IsAuthenticated == true)
            LogEvents.For<SignIn>(WebEvents.Logout).Information(
                "Web logout by account {AccountId}", http.User.AccountId());
        await http.SignOutAsync(AuthSetup.Scheme);
    }
}
