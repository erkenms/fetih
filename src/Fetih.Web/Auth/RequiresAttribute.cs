namespace Fetih.Web.Auth;

// What a page or one of its handlers needs, checked by PageChecks before the handler runs.
// Razor Pages can't put [Authorize] on one handler, so this goes on every OnPost… method
// (a test enforces it) and on the page model when the page itself needs more than its folder.
// [Requires] with no permission means any signed-in user.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresAttribute : Attribute
{
    public RequiresAttribute()
    {
    }

    public RequiresAttribute(Permission permission) => this.Permission = permission;

    public Permission? Permission { get; }

    // The handler writes the game's data files, which Web:StudioReadOnly forbids
    public bool StudioWrite { get; init; }
}
