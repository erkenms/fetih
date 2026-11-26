namespace Fetih.Web.Pages.Admin.Pickers;

using Fetih.Web.Auth;
using Fetih.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.ViewCatalogs)]
public class IndexModel(PickerSearch search) : PageModel
{
    public PickerOption[] Options { get; private set; } = [];

    // /admin/pickers?kind=Item&q=meteor
    public void OnGet(PickerKind kind, string? q) => this.Options = search.Find(kind, q).ToArray();
}
