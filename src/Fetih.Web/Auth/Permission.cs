namespace Fetih.Web.Auth;

using Fetih.Game.States;

// What a web user may do. Endpoints require a permission, never an authority level, so
// moving a permission between roles is a change to Grants alone.
public enum Permission
{
    AdminArea,
    ViewCatalogs,
    ViewLive,
    Moderate,
    ManageCharacters,
    ViewLogs,
    ViewAudit,
    Studio,
    ManageAccounts,
    ServerControl,
}

public static class Permissions
{
    public static Authority Minimum(Permission permission) => permission switch
    {
        Permission.AdminArea => Authority.Moderator,
        Permission.ViewCatalogs => Authority.Moderator,
        Permission.ViewLive => Authority.Moderator,
        Permission.Moderate => Authority.Moderator,
        Permission.ViewLogs => Authority.Moderator,
        Permission.ManageCharacters => Authority.GameMaster,
        Permission.ViewAudit => Authority.GameMaster,
        Permission.Studio => Authority.ProjectManager,
        Permission.ManageAccounts => Authority.Administrator,
        Permission.ServerControl => Authority.Administrator,
        _ => Authority.Administrator,
    };

    public static IEnumerable<Permission> Granted(Authority authority) =>
        Enum.GetValues<Permission>().Where(p => authority >= Minimum(p));

    public static string Policy(Permission permission) => "permission:" + permission;

    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(Policy(permission));
}
