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
            new("Canlı dünya", "/admin/live", Permission.ViewLive),
        ]),
        new("Oyuncular",
        [
            new("Oyuncular", "/admin/players", Permission.AdminArea),
        ]),
        new("Stüdyo",
        [
            new("Değişiklikler", "/admin/changes", Permission.Studio),
            new("Veri düzenleyiciler", "/admin/data", Permission.Studio),
            new("Canavarlar", "/admin/monsters", Permission.Studio),
            new("Haritalar", "/admin/maps", Permission.ViewCatalogs),
            new("NPC konuşmaları", "/admin/npc-dialogs", Permission.ViewCatalogs),
            new("Bütünlük", "/admin/integrity", Permission.Studio),
        ]),
        new("Başvuru",
        [
            new("Eşyalar", "/admin/items", Permission.ViewCatalogs),
            new("Büyüler", "/admin/catalogs/spells", Permission.ViewCatalogs),
            new("Dükkânlar", "/admin/catalogs/shops", Permission.ViewCatalogs),
            new("Canavar kataloğu", "/admin/catalogs/monsters", Permission.ViewCatalogs),
            new("Harita kataloğu", "/admin/catalogs/maps", Permission.ViewCatalogs),
            new("Yüzler", "/admin/catalogs/faces", Permission.ViewCatalogs),
            new("Görünüşler", "/admin/catalogs/looks", Permission.ViewCatalogs),
        ]),
    ];

    public static IEnumerable<Section> For(System.Security.Claims.ClaimsPrincipal user) =>
        Sections.Select(s => s with { Links = s.Links.Where(l => user.Has(l.Permission)).ToArray() })
            .Where(s => s.Links.Length > 0);
}
