namespace Comet.Web.Auth;

using System.Security.Claims;
using Comet.Account.Database.Models;
using Comet.Game.States;

public static class WebUser
{
    public const string AuthorityClaim = "authority";
    public const string CheckedClaim = "checked";

    public static ClaimsPrincipal Create(DbAccount account) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.AccountID.ToString()),
            new Claim(ClaimTypes.Name, account.Username),
            new Claim(AuthorityClaim, account.AuthorityID.ToString()),
            new Claim(CheckedClaim, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
        }, AuthSetup.Scheme));

    public static uint AccountId(this ClaimsPrincipal user) =>
        uint.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out uint id) ? id : 0;

    public static string Username(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name) ?? "";

    public static Authority Authority(this ClaimsPrincipal user) =>
        ushort.TryParse(user.FindFirstValue(AuthorityClaim), out ushort a) ? (Authority)a : 0;

    public static bool Has(this ClaimsPrincipal user, Permission permission) =>
        user.Identity?.IsAuthenticated == true && user.Authority() >= Permissions.Minimum(permission);
}
