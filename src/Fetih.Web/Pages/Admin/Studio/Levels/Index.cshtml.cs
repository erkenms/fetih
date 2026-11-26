namespace Fetih.Web.Pages.Admin.Studio.Levels;

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
        var data = dataEdit.ReadLevels();
        this.Hash = data.Hash;
        this.Rows = data.Entries.Select(e => new RowInput
        {
            Level = e.Level,
            Experience = e.Experience,
            UpLevTime = e.UpLevTime,
        }).ToList();
        this.Problems = data.Problems;
        this.MarkBadCells(this.Problems);
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        var kept = this.Rows.Where(r => !r.Delete).ToList();
        var request = new SaveLevelsRequest(
            this.Hash,
            kept.Select(r => new LevelEntryInput(r.Level, r.Experience, r.UpLevTime)).ToArray());

        try
        {
            var result = await dataEdit.SaveLevelsAsync(request, audit, ct);
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
                this.Flash($"levels.json kaydedildi (+{result.Added} ~{result.Updated} -{result.Removed}).");
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
            if (p.Message.Contains("Experience", StringComparison.OrdinalIgnoreCase))
                this.BadCells.Add((i, "Experience"));
            else if (p.Message.Contains("Level", StringComparison.OrdinalIgnoreCase) || p.Message.Contains("already used", StringComparison.OrdinalIgnoreCase))
                this.BadCells.Add((i, "Level"));
            else
                this.BadCells.Add((i, "*"));
        }
    }

    public sealed class RowInput
    {
        public byte Level { get; set; }
        public ulong Experience { get; set; }
        public uint UpLevTime { get; set; }
        public bool Delete { get; set; }
    }
}
