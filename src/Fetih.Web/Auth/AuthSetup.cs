namespace Fetih.Web.Auth;

using System.Security.Claims;
using Fetih.Account.Database.Models;
using Fetih.Web.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

public static class AuthSetup
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    // A banned, locked or demoted account loses its session within this time
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(60);

    public static void AddWebAuth(this IServiceCollection services, WebConfiguration config)
    {
        string? keysDir = WebConfiguration.PathsSection.Resolve(config.Paths.KeysDirectory);
        if (keysDir != null)
        {
            Directory.CreateDirectory(keysDir);
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
                .SetApplicationName("Fetih.Web");
        }

        services.AddAuthentication(Scheme).AddCookie(options =>
        {
            options.Cookie.Name = "fetih.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = config.Web.SecureCookies
                ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromDays(7);
            options.SlidingExpiration = true;
            options.LoginPath = "/giris";
            options.ReturnUrlParameter = "donus";
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
            options.Events.OnValidatePrincipal = RecheckAsync;
        });

        services.AddAuthorization(options =>
        {
            foreach (var permission in Enum.GetValues<Permission>())
                options.AddPolicy(Permissions.Policy(permission),
                    policy => policy.RequireAuthenticatedUser()
                        .RequireAssertion(context => context.User.Has(permission)));
        });
    }

    private static async Task RecheckAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal == null)
            return;
        long.TryParse(principal.FindFirstValue(WebUser.CheckedClaim), out long checkedAt);
        if (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(checkedAt) < RecheckInterval)
            return;

        var accounts = context.HttpContext.RequestServices.GetRequiredService<AccountStore>();
        var config = context.HttpContext.RequestServices.GetRequiredService<WebConfiguration>();
        DbAccount? account = await accounts.FindAsync(principal.AccountId());
        if (account == null || AccountStore.LoginRefusal(account) != null ||
            (config.Web.StaffOnly && (Fetih.Game.States.Authority)account.AuthorityID < Fetih.Game.States.Authority.Moderator))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Scheme);
            return;
        }

        context.ReplacePrincipal(WebUser.Create(account));
        context.ShouldRenew = true;
    }
}
