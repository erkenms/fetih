namespace Fetih.Web.Pages.Admin.Studio.Goals;

using Fetih.Game.DataEditing;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Fetih.Web.Features.DataEdit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.Studio)]
[RequestFormLimits(ValueCountLimit = 20000)]
public class IndexModel(WebConfiguration config, DataEditService dataEdit, AuditLog audit) : PageModel
{
    public bool StudioReadOnly => config.Web.StudioReadOnly;

    [BindProperty]
    public string Hash { get; set; } = "";

    [BindProperty]
    public List<RowInput> Rows { get; set; } = [];

    public DataProblemDto[] Problems { get; private set; } = [];
    public string? ConflictError { get; private set; }
    public HashSet<(int Row, string Field)> BadCells { get; private set; } = [];

    public void OnGet()
    {
        var data = dataEdit.ReadGoals();
        this.Hash = data.Hash;
        this.Rows = data.Entries.Select(e => new RowInput
        {
            Id = e.Id,
            Lookface = e.Lookface,
            Role = e.Role,
            Level = e.Level,
            Life = e.Life,
            Map = e.Map,
            X = e.X,
            Y = e.Y,
        }).ToList();
        this.Problems = data.Problems;
        this.MarkBadCells(this.Problems);
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        var kept = this.Rows.Where(r => !r.Delete).ToList();
        var request = new SaveGoalsRequest(
            this.Hash,
            kept.Select(r => new GoalEntryInput(r.Id, r.Lookface, r.Role, r.Level, r.Life, r.Map, r.X, r.Y)).ToArray());

        try
        {
            var result = await dataEdit.SaveGoalsAsync(request, audit, ct);
            if (!result.Saved)
            {
                this.Rows = kept;
                this.Hash = result.Hash;
                this.Problems = result.Problems;
                this.MarkBadCells(this.Problems);
                return this.Page();
            }

            if (result.Added + result.Updated + result.Removed == 0)
                this.Flash("Değişiklik yok.");
            else
                this.Flash($"goals.json kaydedildi (+{result.Added} ~{result.Updated} -{result.Removed}).");
            return this.RedirectToPage();
        }
        catch (DataFileConflictException)
        {
            this.Rows = kept;
            this.ConflictError = "Dosya başka biri tarafından değiştirildi. Sayfayı yenileyip tekrar dene.";
            return this.Page();
        }
    }

    public bool IsBad(int row, string field) =>
        this.BadCells.Contains((row, field)) || this.BadCells.Contains((row, "*"));

    private void MarkBadCells(DataProblemDto[] problems)
    {
        this.BadCells = [];
        foreach (var p in problems.Where(x => x.Level == "error"))
        {
            if (p.Entry is not int i)
                continue;
            if (p.Message.Contains("NPC ID", StringComparison.OrdinalIgnoreCase)
                || p.Message.Contains("already used", StringComparison.OrdinalIgnoreCase)
                || p.Message.Contains("also in", StringComparison.OrdinalIgnoreCase))
            {
                this.BadCells.Add((i, "Id"));
            }
            else if (p.Message.Contains("outside map", StringComparison.OrdinalIgnoreCase)
                     || p.Message.Contains("GameMap.dat", StringComparison.OrdinalIgnoreCase)
                     || p.Message.Contains("walkable", StringComparison.OrdinalIgnoreCase))
            {
                this.BadCells.Add((i, "Map"));
                this.BadCells.Add((i, "X"));
                this.BadCells.Add((i, "Y"));
            }
            else
            {
                this.BadCells.Add((i, "*"));
            }
        }
    }

    public sealed class RowInput
    {
        public uint Id { get; set; }
        public ushort Lookface { get; set; }
        public byte Role { get; set; } = 21;
        public byte Level { get; set; } = 20;
        public uint Life { get; set; } = 10000;
        public uint Map { get; set; } = 1039;
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public bool Delete { get; set; }
    }
}
