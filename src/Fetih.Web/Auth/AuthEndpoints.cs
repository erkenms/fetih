namespace Fetih.Web.Auth;

public static class AuthEndpoints
{
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
}
