namespace Fetih.Web.Pages.Admin.Studio.Teleporters;

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
    public HashSet<(int Row, int Dest)> BadDestinations { get; private set; } = [];

    public void OnGet()
    {
        var data = dataEdit.ReadTeleporters();
        this.Hash = data.Hash;
        this.Rows = data.Entries.Select(e => new RowInput
        {
            Id = e.Id,
            Fare = e.Fare,
            Face = e.Face,
            Text = string.Join("\n", e.Text),
            Close = e.Close,
            Location = e.NpcMap is { } m ? $"#{m} ({e.NpcX}, {e.NpcY})" : null,
            Destinations = e.Destinations.Select(d => new DestInput
            {
                Name = d.Name,
                Map = d.Map,
                X = d.X,
                Y = d.Y,
            }).ToList(),
        }).ToList();
        this.Problems = data.Problems;
        this.MarkBadCells(this.Problems);
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        var kept = this.Rows.Where(r => !r.Delete).Select(r => new RowInput
        {
            Id = r.Id,
            Fare = r.Fare,
            Face = r.Face,
            Text = r.Text ?? "",
            Close = r.Close,
            Location = r.Location,
            Destinations = (r.Destinations ?? []).Where(d => !d.Delete).ToList(),
        }).ToList();

        // Check every destination against the map grid; walls and out-of-bounds targets refuse the save.
        var cellErrors = new List<DataProblemDto>();
        this.BadDestinations = [];
        for (int i = 0; i < kept.Count; i++)
        {
            var dests = kept[i].Destinations;
            for (int j = 0; j < dests.Count; j++)
            {
                var d = dests[j];
                var cell = dataEdit.CheckCell(d.Map, d.X, d.Y);
                if (!cell.HasGrid)
                    continue;
                if (!cell.InBounds)
                {
                    this.BadDestinations.Add((i, j));
                    cellErrors.Add(new DataProblemDto(DataFileKinds.Teleporters.FileName, i, null,
                        $"Hedef '{d.Name}' ({d.X}, {d.Y}) harita {d.Map} sınırları dışında.", "error"));
                }
                else if (!cell.Walkable)
                {
                    this.BadDestinations.Add((i, j));
                    cellErrors.Add(new DataProblemDto(DataFileKinds.Teleporters.FileName, i, null,
                        $"Hedef '{d.Name}' ({d.X}, {d.Y}) harita {d.Map} üzerinde yürünebilir değil (duvar/engel).", "error"));
                }
            }
        }

        if (cellErrors.Count > 0)
        {
            this.Rows = kept;
            this.Problems = [.. cellErrors];
            return this.Page();
        }

        var request = new SaveTeleportersRequest(
            this.Hash,
            kept.Select(r => new TeleporterEntryInput(
                r.Id,
                r.Fare,
                r.Face,
                SplitLines(r.Text),
                r.Destinations.Select(d => new TeleporterDestinationDto(d.Name ?? "", d.Map, d.X, d.Y)).ToArray(),
                string.IsNullOrWhiteSpace(r.Close) ? null : r.Close)).ToArray());

        try
        {
            var result = await dataEdit.SaveTeleportersAsync(request, audit, ct);
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
                this.Flash($"teleporters.json kaydedildi (+{result.Added} ~{result.Updated} -{result.Removed}).");
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

    public bool IsBadDest(int row, int dest) => this.BadDestinations.Contains((row, dest));

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
            else if (p.Message.Contains("destination", StringComparison.OrdinalIgnoreCase)
                     || p.Message.Contains("Map ", StringComparison.OrdinalIgnoreCase)
                     || p.Message.Contains("outside map", StringComparison.OrdinalIgnoreCase))
            {
                this.BadCells.Add((i, "Destinations"));
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
        public uint Fare { get; set; } = 100;
        public byte Face { get; set; } = 1;
        public string? Text { get; set; }
        public string? Close { get; set; }
        public string? Location { get; set; }
        public List<DestInput> Destinations { get; set; } = [];
        public bool Delete { get; set; }
    }

    public sealed class DestInput
    {
        public string? Name { get; set; }
        public uint Map { get; set; } = 1002;
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public bool Delete { get; set; }
    }
}
