namespace Fetih.Web.Pages.Admin.Studio.Talkers;

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
        var data = dataEdit.ReadTalkers();
        this.Hash = data.Hash;
        this.Rows = data.Entries.Select(e => new RowInput
        {
            Id = e.Id,
            Face = e.Face,
            Text = string.Join("\n", e.Text),
            Reply = e.Reply,
            Location = e.NpcMap is { } m ? $"#{m} ({e.NpcX}, {e.NpcY})" : null,
        }).ToList();
        this.Problems = data.Problems;
        this.MarkBadCells(this.Problems);
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        var kept = this.Rows.Where(r => !r.Delete).ToList();
        var request = new SaveTalkersRequest(
            this.Hash,
            kept.Select(r => new TalkerEntryInput(r.Id, r.Face, SplitLines(r.Text), r.Reply ?? "")).ToArray());

        try
        {
            var result = await dataEdit.SaveTalkersAsync(request, audit, ct);
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
                this.Flash($"talkers.json kaydedildi (+{result.Added} ~{result.Updated} -{result.Removed}).");
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
            if (p.Message.Contains("NPC", StringComparison.OrdinalIgnoreCase)
                || p.Message.Contains("already used", StringComparison.OrdinalIgnoreCase))
            {
                this.BadCells.Add((i, "Id"));
            }
            else if (p.Message.Contains("No text", StringComparison.OrdinalIgnoreCase))
            {
                this.BadCells.Add((i, "Text"));
            }
            else if (p.Message.Contains("No reply", StringComparison.OrdinalIgnoreCase))
            {
                this.BadCells.Add((i, "Reply"));
            }
            else
            {
                this.BadCells.Add((i, "*"));
            }
        }
    }

    private static string[] SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        var lines = text.Replace("\r\n", "\n").Split('\n');
        int end = lines.Length;
        while (end > 0 && lines[end - 1].Length == 0)
            end--;
        return lines[..end];
    }

    public sealed class RowInput
    {
        public uint Id { get; set; }
        public byte Face { get; set; }
        public string? Text { get; set; }
        public string? Reply { get; set; }
        public string? Location { get; set; }
        public bool Delete { get; set; }
    }
}
