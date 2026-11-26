namespace Fetih.Web.Pages.Shared;

using Fetih.Web.Auth;

// The admin menu, one line per page, whether the page is Razor or still React. A page that
// moves keeps its line (fix the URL if it changes); a page that's deleted deletes its line.
// The Grafana links are in _GrafanaLinks.cshtml.
public static class AdminMenu
{
    public sealed record Link(string Text, string Url, Permission Permission);

    public sealed record Section(string Title, Link[] Links);

    public static readonly Section[] Sections =
    [
        new("Dünya",
        [
            new("Duyurular", "/admin/broadcasts", Permission.Moderate),
            new("Sunucu", "/admin/server", Permission.ServerControl),
            new("İlan panosu", "/admin/board", Permission.Moderate),
        ]),
        new("Oyuncular",
        [
            new("Oyuncular", "/admin/players", Permission.AdminArea),
        ]),
        new("Stüdyo",
        [
            new("Değişiklikler", "/admin/studio/changes", Permission.Studio),
            new("Seviyeler", "/admin/studio/levels", Permission.Studio),
            new("Hedefler", "/admin/studio/goals", Permission.Studio),
            new("Işınlayıcılar", "/admin/studio/teleporters", Permission.Studio),
            new("Konuşmacılar", "/admin/studio/talkers", Permission.Studio),
            new("Canavarlar", "/admin/studio/monsters", Permission.ViewCatalogs),
            new("Haritalar", "/admin/maps", Permission.ViewCatalogs),
        ]),
        new("Başvuru",
        [
            new("Eşyalar", "/admin/items", Permission.ViewCatalogs),
            new("Büyüler", "/admin/catalogs/spells", Permission.ViewCatalogs),
            new("Dükkânlar", "/admin/catalogs/shops", Permission.ViewCatalogs),
        ]),
    ];

    public static IEnumerable<Section> For(System.Security.Claims.ClaimsPrincipal user) =>
        Sections.Select(s => s with { Links = s.Links.Where(l => user.Has(l.Permission)).ToArray() })
            .Where(s => s.Links.Length > 0);
}
