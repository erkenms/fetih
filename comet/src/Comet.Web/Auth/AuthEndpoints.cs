namespace Comet.Web.Auth;

using Comet.Core.Logging;
using Comet.Game.States;
using Comet.Web.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

public record LoginRequest(string Username, string Password);

public record MeResponse(uint AccountId, string Username, int Authority, Permission[] Permissions,
    bool StudioReadOnly = false);

public static class AuthEndpoints
{
    public static void MapAuth(this RouteGroupBuilder api)
    {
        api.MapPost("/auth/login", LoginAsync).RequireRateLimiting(AuthSetup.LoginRateLimit)
            .WithName("login").AllowAnonymous()
            .Produces<ApiError>(StatusCodes.Status401Unauthorized)
            .Produces<ApiError>(StatusCodes.Status403Forbidden);
        api.MapPost("/auth/logout", (Delegate)LogoutAsync).WithName("logout").AllowAnonymous();
        api.MapGet("/me", Me).WithName("me").RequireAuthorization();
    }

    private static async Task<Results<Ok<MeResponse>, JsonHttpResult<ApiError>>> LoginAsync(
        LoginRequest request, HttpContext http, AccountStore accounts, WebConfiguration config)
    {
        string? ip = http.Connection.RemoteIpAddress?.ToString();
        var account = await accounts.FindAsync(request.Username);
        if (!Passwords.Matches(account, request.Password))
        {
            LogEvents.For(typeof(AuthEndpoints), WebEvents.LoginFailed).Warning(
                "Web login failed for {Username} from {IPAddress}", request.Username, ip);
            return ApiError.Result("invalid_credentials", StatusCodes.Status401Unauthorized);
        }

        if (AccountStore.LoginRefusal(account) is string refusal)
        {
            LogEvents.For(typeof(AuthEndpoints), WebEvents.LoginFailed).Warning(
                "Web login refused for account {AccountId} from {IPAddress}: {Reason}",
                account.AccountID, ip, refusal);
            return ApiError.Result(refusal, StatusCodes.Status403Forbidden);
        }

        if (config.Web.StaffOnly && (Authority)account.AuthorityID < Authority.Moderator)
        {
            LogEvents.For(typeof(AuthEndpoints), WebEvents.LoginFailed).Warning(
                "Web login refused for account {AccountId} from {IPAddress}: {Reason}",
                account.AccountID, ip, "staff_only");
            return ApiError.Result("staff_only", StatusCodes.Status403Forbidden);
        }

        var user = WebUser.Create(account);
        await http.SignInAsync(AuthSetup.Scheme, user);
        LogEvents.For(typeof(AuthEndpoints), WebEvents.Login).Information(
            "Web login by account {AccountId} from {IPAddress}", account.AccountID, ip);
        return TypedResults.Ok(ToMe(user, config.Web.StudioReadOnly));
    }

    private static async Task<NoContent> LogoutAsync(HttpContext http)
    {
        if (http.User.Identity?.IsAuthenticated == true)
            LogEvents.For(typeof(AuthEndpoints), WebEvents.Logout).Information(
                "Web logout by account {AccountId}", http.User.AccountId());
        await http.SignOutAsync(AuthSetup.Scheme);
        return TypedResults.NoContent();
    }

    private static Ok<MeResponse> Me(HttpContext http, WebConfiguration config) =>
        TypedResults.Ok(ToMe(http.User, config.Web.StudioReadOnly));

    private static MeResponse ToMe(System.Security.Claims.ClaimsPrincipal user, bool studioReadOnly) => new(
        user.AccountId(), user.Username(), (int)user.Authority(),
        Permissions.Granted(user.Authority()).ToArray(), studioReadOnly);
}
