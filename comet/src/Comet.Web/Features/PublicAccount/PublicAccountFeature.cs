namespace Comet.Web.Features.PublicAccount;

using System.Threading.RateLimiting;
using Comet.Account.Database;
using Comet.Account.Database.Repositories;
using Comet.Web.Accounts;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using GameDbContext = Comet.Game.Database.ServerDbContext;

public record PublicAccountConfigResponse(
    bool CaptchaEnabled,
    string? CaptchaProvider,
    string? CaptchaSiteKey,
    int MinPasswordLength,
    int MaxPasswordLength,
    int MaxUsernameLength);

public record RegisterRequest(string Username, string Password, string? CaptchaToken = null);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record MyCharacterSummary(
    uint CharacterId,
    string Name,
    byte Level,
    byte Rebirths,
    byte CurrentClass,
    uint MapId,
    uint Silver,
    uint Jewels,
    uint VipLevel,
    string Spouse,
    DateTime Registered);

public record LoginHistoryEntry(DateTime Timestamp, string IpAddress);

public record MyAccountResponse(
    uint AccountId,
    string Username,
    int Authority,
    int Status,
    DateTime Registered,
    MyCharacterSummary[] Characters,
    LoginHistoryEntry[] Logins);

public sealed class PublicAccountFeature : IFeature
{
    public const string RegisterRateLimit = "public-register";
    public const string PasswordRateLimit = "public-password";

    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddOptions<PublicAccountOptions>().BindConfiguration("PublicAccount");
        services.AddHttpClient("captcha");
        services.AddSingleton<ICaptchaVerifier, HttpCaptchaVerifier>();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RegisterRateLimit, context =>
            {
                var limit = context.RequestServices.GetRequiredService<IOptions<PublicAccountOptions>>()
                    .Value.RegisterAttemptsPerMinute;
                return FixedWindow(context, limit);
            });
            options.AddPolicy(PasswordRateLimit, context =>
            {
                var limit = context.RequestServices.GetRequiredService<IOptions<PublicAccountOptions>>()
                    .Value.PasswordChangesPerMinute;
                return FixedWindow(context, limit);
            });
        });
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/public/account");
        group.MapGet("/config", GetConfig)
            .WithName("publicAccountConfig")
            .AllowAnonymous();
        group.MapPost("/register", RegisterAsync)
            .RequireRateLimiting(RegisterRateLimit)
            .WithName("registerAccount")
            .AllowAnonymous()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status409Conflict);
        group.MapGet("/profile", GetProfileAsync)
            .WithName("getMyAccount")
            .RequireAuthorization()
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        group.MapPost("/password", ChangePasswordAsync)
            .RequireRateLimiting(PasswordRateLimit)
            .WithName("changeMyPassword")
            .RequireAuthorization()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    private static RateLimitPartition<string> FixedWindow(HttpContext context, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, permitLimit),
                Window = TimeSpan.FromMinutes(1),
            });

    private static Ok<PublicAccountConfigResponse> GetConfig(IOptions<PublicAccountOptions> options)
    {
        var settings = options.Value;
        return TypedResults.Ok(new PublicAccountConfigResponse(
            settings.CaptchaEnabled,
            settings.CaptchaEnabled ? settings.CaptchaProvider : null,
            settings.CaptchaEnabled ? settings.CaptchaSiteKey : null,
            AccountRules.MinPasswordLength,
            AccountRules.MaxPasswordLength,
            AccountRules.MaxUsernameLength));
    }

    private static async Task<Results<Created<MeResponse>, JsonHttpResult<ApiError>>> RegisterAsync(
        RegisterRequest request,
        HttpContext http,
        WebConfiguration config,
        IOptions<PublicAccountOptions> options,
        ICaptchaVerifier captcha,
        AuditLog audit,
        CancellationToken ct)
    {
        if (config.Web.StaffOnly)
            return ApiError.Result("staff_only", StatusCodes.Status403Forbidden);

        if (options.Value.CaptchaEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.CaptchaToken))
                return ApiError.Result("captcha_required", StatusCodes.Status400BadRequest);

            string? ip = http.Connection.RemoteIpAddress?.ToString();
            if (!await captcha.VerifyAsync(request.CaptchaToken, ip, ct))
                return ApiError.Result("captcha_failed", StatusCodes.Status400BadRequest);
        }

        var (account, error) = await AccountsRepository.CreateAsync(request.Username, request.Password);
        if (error == "username_taken")
            return ApiError.Result("username_taken", StatusCodes.Status409Conflict);
        if (error != null || account == null)
            return ApiError.Result(error ?? "invalid_username", StatusCodes.Status400BadRequest);

        var user = WebUser.Create(account);
        http.User = user;
        await http.SignInAsync(AuthSetup.Scheme, user);
        await audit.WriteAsync("account.register", "account", account.AccountID.ToString(),
            summary: account.Username);

        var me = new MeResponse(
            user.AccountId(),
            user.Username(),
            (int)user.Authority(),
            Permissions.Granted(user.Authority()).ToArray(),
            config.Web.StudioReadOnly);
        return TypedResults.Created("/api/public/account/profile", me);
    }

    private static async Task<Results<Ok<MyAccountResponse>, JsonHttpResult<ApiError>>> GetProfileAsync(
        HttpContext http,
        CancellationToken ct)
    {
        uint accountId = http.User.AccountId();

        await using var accountDb = new ServerDbContext();
        var account = await accountDb.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AccountID == accountId, ct);
        if (account == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);

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

        return TypedResults.Ok(new MyAccountResponse(
            account.AccountID,
            account.Username,
            account.AuthorityID,
            account.StatusID,
            DateTime.SpecifyKind(account.Registered, DateTimeKind.Utc),
            characters,
            logins));
    }

    private static async Task<Results<NoContent, JsonHttpResult<ApiError>>> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext http,
        AccountStore accounts,
        AuditLog audit)
    {
        var account = await accounts.FindAsync(http.User.AccountId());
        if (account == null)
            return ApiError.Result("unauthorized", StatusCodes.Status401Unauthorized);

        if (!Passwords.Matches(account, request.CurrentPassword))
            return ApiError.Result("wrong_password", StatusCodes.Status400BadRequest);

        if (AccountRules.CheckPassword(request.NewPassword) is string validationError)
            return ApiError.Result(validationError, StatusCodes.Status400BadRequest);

        string? error = await AccountsRepository.SetPasswordAsync(account.Username, request.NewPassword);
        if (error != null)
            return ApiError.Result(error, StatusCodes.Status400BadRequest);

        await audit.WriteAsync("account.change_password", "account", account.AccountID.ToString(),
            summary: "Password changed by owner");
        return TypedResults.NoContent();
    }
}
