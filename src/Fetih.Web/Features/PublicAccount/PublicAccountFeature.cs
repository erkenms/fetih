namespace Fetih.Web.Features.PublicAccount;

using System.Threading.RateLimiting;
using Fetih.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

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
        services.AddSingleton<PublicAccountService>();

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
        group.MapGet("/config", (PublicAccountService service) => TypedResults.Ok(service.GetConfig()))
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

    private static async Task<Results<Created<MeResponse>, JsonHttpResult<ApiError>>> RegisterAsync(
        RegisterRequest request,
        HttpContext http,
        WebConfiguration config,
        PublicAccountService service,
        CancellationToken ct)
    {
        var (account, error) = await service.RegisterAsync(http, request.Username, request.Password, request.CaptchaToken, ct);
        if (error == "staff_only")
            return ApiError.Result("staff_only", StatusCodes.Status403Forbidden);
        if (error == "username_taken")
            return ApiError.Result("username_taken", StatusCodes.Status409Conflict);
        if (error != null || account == null)
            return ApiError.Result(error ?? "invalid_username", StatusCodes.Status400BadRequest);

        var user = http.User;
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
        PublicAccountService service,
        CancellationToken ct)
    {
        var profile = await service.GetProfileAsync(http.User.AccountId(), ct);
        return profile == null ? ApiError.Result("not_found", StatusCodes.Status404NotFound) : TypedResults.Ok(profile);
    }

    private static async Task<Results<NoContent, JsonHttpResult<ApiError>>> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext http,
        PublicAccountService service)
    {
        string? error = await service.ChangePasswordAsync(http.User.AccountId(), request.CurrentPassword, request.NewPassword);
        if (error == "unauthorized")
            return ApiError.Result("unauthorized", StatusCodes.Status401Unauthorized);
        if (error != null)
            return ApiError.Result(error, StatusCodes.Status400BadRequest);
        return TypedResults.NoContent();
    }
}
