namespace Fetih.Web.Auth;

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
        LoginRequest request, HttpContext http, SignIn signIn, WebConfiguration config)
    {
        return await signIn.TryAsync(http, request.Username, request.Password) switch
        {
            null => TypedResults.Ok(ToMe(http.User, config.Web.StudioReadOnly)),
            "invalid_credentials" => ApiError.Result("invalid_credentials", StatusCodes.Status401Unauthorized),
            "too_many_attempts" => ApiError.Result("too_many_attempts", StatusCodes.Status429TooManyRequests),
            var refusal => ApiError.Result(refusal, StatusCodes.Status403Forbidden),
        };
    }

    private static async Task<NoContent> LogoutAsync(HttpContext http)
    {
        await SignIn.SignOutAsync(http);
        return TypedResults.NoContent();
    }

    private static Ok<MeResponse> Me(HttpContext http, WebConfiguration config) =>
        TypedResults.Ok(ToMe(http.User, config.Web.StudioReadOnly));

    private static MeResponse ToMe(System.Security.Claims.ClaimsPrincipal user, bool studioReadOnly) => new(
        user.AccountId(), user.Username(), (int)user.Authority(),
        Permissions.Granted(user.Authority()).ToArray(), studioReadOnly);
}
