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

    public static void MapAuthCheck(this WebApplication app)
    {
        app.MapGet("/auth/check", Check).AllowAnonymous();
    }

    private static IResult Check(HttpContext http)
    {
        if (http.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } scp)
            scp.Enabled = false;

        if (http.User.Has(Permission.ViewLogs))
        {
            http.Response.Headers["X-Webauth-User"] = http.User.Username();
            return Results.Ok();
        }

        if (http.User.Identity?.IsAuthenticated == true)
            return Results.StatusCode(StatusCodes.Status401Unauthorized);

        string? forwardedUri = http.Request.Headers["X-Forwarded-Uri"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedUri) && forwardedUri.StartsWith('/'))
            return Results.Redirect($"/giris?donus={Uri.EscapeDataString(forwardedUri)}");

        return Results.StatusCode(StatusCodes.Status401Unauthorized);
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
