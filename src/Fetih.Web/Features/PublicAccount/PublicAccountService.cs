namespace Fetih.Web.Features.PublicAccount;

using System.Threading.RateLimiting;
using Fetih.Account.Database;
using Fetih.Account.Database.Models;
using Fetih.Account.Database.Repositories;
using Fetih.Web.Accounts;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using GameDbContext = Fetih.Game.Database.ServerDbContext;

public sealed class PublicAccountService(
    WebConfiguration config,
    IOptions<PublicAccountOptions> options,
    ICaptchaVerifier captcha,
    AccountStore accounts,
    AuditLog audit)
{
    // Per IP for registering, per account for password changes, each per minute
    private readonly PartitionedRateLimiter<string> registrations = PerMinute(options.Value.RegisterAttemptsPerMinute);
    private readonly PartitionedRateLimiter<string> passwordChanges = PerMinute(options.Value.PasswordChangesPerMinute);

    private static PartitionedRateLimiter<string> PerMinute(int limit) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, limit), Window = TimeSpan.FromMinutes(1) }));

    public PublicAccountConfigResponse GetConfig()
    {
        var settings = options.Value;
        return new PublicAccountConfigResponse(
            settings.CaptchaEnabled,
            settings.CaptchaEnabled ? settings.CaptchaProvider : null,
            settings.CaptchaEnabled ? settings.CaptchaSiteKey : null,
            AccountRules.MinPasswordLength,
            AccountRules.MaxPasswordLength,
            AccountRules.MaxUsernameLength);
    }

    public async Task<(DbAccount? Account, string? Error)> RegisterAsync(
        HttpContext http, string? username, string? password, string? captchaToken, CancellationToken ct = default)
    {
        if (config.Web.StaffOnly)
            return (null, "staff_only");

        using (var lease = this.registrations.AttemptAcquire(http.Connection.RemoteIpAddress?.ToString() ?? ""))
            if (!lease.IsAcquired)
                return (null, "too_many_attempts");

        if (options.Value.CaptchaEnabled)
        {
            if (string.IsNullOrWhiteSpace(captchaToken))
                return (null, "captcha_required");

            string? ip = http.Connection.RemoteIpAddress?.ToString();
            if (!await captcha.VerifyAsync(captchaToken, ip, ct))
                return (null, "captcha_failed");
        }

        var (account, error) = await AccountsRepository.CreateAsync(username ?? "", password ?? "");
        if (error != null || account == null)
            return (null, error ?? "invalid_username");

        var user = WebUser.Create(account);
        http.User = user;
        await http.SignInAsync(AuthSetup.Scheme, user);
        await audit.WriteAsync("account.register", "account", account.AccountID.ToString(),
            summary: account.Username);
        return (account, null);
    }

    public async Task<MyAccountResponse?> GetProfileAsync(uint accountId, CancellationToken ct = default)
    {
        await using var accountDb = new ServerDbContext();
        var account = await accountDb.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AccountID == accountId, ct);
        if (account == null)
            return null;

        var logins = await accountDb.Logins.AsNoTracking()
            .Where(x => x.AccountID == accountId)
            .OrderByDescending(x => x.Timestamp)
            .Take(50)
            .Select(x => new LoginHistoryEntry(
                DateTime.SpecifyKind(x.Timestamp, DateTimeKind.Utc),
                x.IPAddress))
            .ToArrayAsync(ct);

        await using var gameDb = new GameDbContext();
        var characters = await gameDb.Characters.AsNoTracking()
            .Where(x => x.AccountID == accountId)
            .OrderByDescending(x => x.Level)
            .ThenBy(x => x.Name)
            .Select(x => new MyCharacterSummary(
                x.CharacterID,
                x.Name,
                x.Level,
                x.Rebirths,
                x.CurrentClass,
                x.MapID,
                x.Silver,
                x.Jewels,
                x.VIPLevel,
                x.Spouse,
                DateTime.SpecifyKind(x.Registered, DateTimeKind.Utc)))
            .ToArrayAsync(ct);

        return new MyAccountResponse(
            account.AccountID,
            account.Username,
            account.AuthorityID,
            account.StatusID,
            DateTime.SpecifyKind(account.Registered, DateTimeKind.Utc),
            characters,
            logins);
    }

    public async Task<string?> ChangePasswordAsync(uint accountId, string? currentPassword, string? newPassword)
    {
        using (var lease = this.passwordChanges.AttemptAcquire(accountId.ToString()))
            if (!lease.IsAcquired)
                return "too_many_attempts";

        var account = await accounts.FindAsync(accountId);
        if (account == null)
            return "unauthorized";

        if (!Passwords.Matches(account, currentPassword))
            return "wrong_password";

        if (AccountRules.CheckPassword(newPassword ?? "") is string validationError)
            return validationError;

        string? error = await AccountsRepository.SetPasswordAsync(account.Username, newPassword ?? "");
        if (error != null)
            return error;

        await audit.WriteAsync("account.change_password", "account", account.AccountID.ToString(),
            summary: "Password changed by owner");
        return null;
    }
}
