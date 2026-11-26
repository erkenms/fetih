namespace Fetih.Web.Pages.Admin.Maps;

using Fetih.Web.Auth;
using Fetih.Web.Features.MapEdit;
using Fetih.Web.Features.Maps;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.ViewCatalogs)]
public class IndexModel(MapDataStore mapData, MapEditService mapEdit) : PageModel
{
    public sealed record MapRow(
        uint Id,
        string DisplayName,
        string? ServerName,
        string[] Aliases,
        bool HasTiles,
        int NpcCount,
        int SpawnCount,
        int PortalCount,
        int Errors,
        int Warnings);

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyProblems { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyWithEntities { get; set; }

    public MapRow[] Maps { get; private set; } = [];

    public int TotalCount { get; private set; }

    public void OnGet()
    {
        var all = mapData.ListMaps();
        var problems = mapEdit.GetProblemCountsByMap();
        this.TotalCount = all.Length;

        IEnumerable<MapRow> rows = all.Select(m =>
        {
            var p = problems.GetValueOrDefault(m.Id);
            string display = m.Name ?? m.ServerName ?? $"Harita #{m.Id}";
            return new MapRow(
                m.Id,
                display,
                m.ServerName != display ? m.ServerName : null,
                m.Aliases,
                m.HasTiles,
                m.NpcCount,
                m.SpawnCount,
                m.PortalCount,
                p?.Errors ?? 0,
                p?.Warnings ?? 0);
        });

        if (!string.IsNullOrWhiteSpace(this.Q))
        {
            string term = this.Q.Trim();
            bool isId = uint.TryParse(term, out uint id);
            rows = rows.Where(m =>
                (isId && m.Id == id)
                || m.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (m.ServerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || m.Aliases.Any(a => a.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        if (this.OnlyProblems)
            rows = rows.Where(m => m.Errors > 0 || m.Warnings > 0);

        if (this.OnlyWithEntities)
            rows = rows.Where(m => m.NpcCount > 0 || m.SpawnCount > 0 || m.PortalCount > 0);

        this.Maps = [.. rows];
    }
}
