namespace Fetih.Web.Pages.Admin.Studio.Changes;

using Fetih.Game;
using Fetih.Game.DataEditing;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Fetih.Web.Features.Changes;
using Fetih.Web.Features.Integrity;
using Fetih.Web.Game;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.Studio)]
public class IndexModel(
    WebConfiguration config,
    ClientFiles clientFiles,
    GitDataService git,
    GameRpc gameRpc,
    AuditLog audit) : PageModel
{
    public bool StudioReadOnly => config.Web.StudioReadOnly;
    public bool GameConnected => gameRpc.Connected;
    public bool GitAvailable { get; private set; }
    public IntegrityReportResponse Integrity { get; private set; } = new("", false, 0, 0, 0, [], []);
    public ChangedFile[] ChangedFiles { get; private set; } = [];

    public sealed record ChangedFile(
        string File,
        string GitStatus,
        int AddedLines,
        int RemovedLines,
        bool Reloadable,
        string Diff);

    public async Task OnGetAsync(CancellationToken ct)
    {
        string dataDir = StudioData.ResolveDataDirectory(config);
        this.Integrity = IntegrityChecker.Run(config, clientFiles);

        var (gitAvailable, statusByFile) = Directory.Exists(dataDir)
            ? await git.GetStatusAsync(dataDir, ct)
            : (false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        this.GitAvailable = gitAvailable;

        var reloadable = await this.GetReloadableFilesAsync();
        var changed = new List<ChangedFile>();
        foreach (var kind in DataFileKinds.All)
        {
            if (!statusByFile.TryGetValue(kind.FileName, out var status) || status == "clean")
                continue;

            string diff = await git.GetDiffAsync(dataDir, kind.FileName, status, ct);
            var (added, removed) = GitDataService.CountDiffLines(diff);
            changed.Add(new ChangedFile(
                kind.FileName,
                status,
                added,
                removed,
                reloadable.Contains(kind.FileName),
                diff));
        }

        this.ChangedFiles = [.. changed];
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostReloadAsync(string file)
    {
        var kind = DataFileKinds.Find(file);
        if (kind == null)
            return this.NotFound();

        if (!DataReload.Files.Contains(kind.FileName, StringComparer.OrdinalIgnoreCase))
        {
            this.FlashError($"{kind.FileName} çalışma zamanında yeniden yüklenemez.");
            return this.RedirectToPage();
        }

        var result = await StudioData.ReloadFileAsync(kind.FileName, gameRpc, audit);
        if (result.Applied)
            this.Flash(string.IsNullOrWhiteSpace(result.Summary)
                ? $"{kind.FileName} yeniden yüklendi."
                : $"{kind.FileName}: {result.Summary}");
        else
            this.FlashError($"{kind.FileName} yüklenemedi: {string.Join("; ", result.Errors)}");

        return this.RedirectToPage();
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostDiscardAsync(string file, CancellationToken ct)
    {
        var kind = DataFileKinds.Find(file);
        if (kind == null)
            return this.NotFound();

        string dataDir = StudioData.ResolveDataDirectory(config);
        var (_, statusByFile) = Directory.Exists(dataDir)
            ? await git.GetStatusAsync(dataDir, ct)
            : (false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        string status = statusByFile.TryGetValue(kind.FileName, out var s) ? s : "clean";
        string diff = status == "clean" ? "" : await git.GetDiffAsync(dataDir, kind.FileName, status, ct);

        bool discarded = await git.DiscardAsync(dataDir, kind.FileName, ct);
        if (discarded)
        {
            await audit.WriteAsync(
                "data.discard",
                "data_file",
                kind.FileName,
                before: string.IsNullOrEmpty(diff) ? null : new { diff },
                summary: $"Discarded local changes in {kind.FileName}");
            this.Flash($"{kind.FileName} değişiklikleri geri alındı.");
        }
        else
        {
            this.FlashError($"{kind.FileName} geri alınamadı.");
        }

        return this.RedirectToPage();
    }

    public static string StatusText(string status) => status switch
    {
        "modified" => "Değiştirildi",
        "added" => "Eklendi",
        "untracked" => "Yeni",
        "deleted" => "Silindi",
        "renamed" => "Yeniden adlandırıldı",
        _ => status,
    };

    public static string DiffLineClass(string line)
    {
        if (line.StartsWith("+++") || line.StartsWith("---") || line.StartsWith("diff ") || line.StartsWith("index "))
            return "diff-meta";
        if (line.StartsWith("@@"))
            return "diff-hunk";
        if (line.StartsWith('+'))
            return "diff-add";
        if (line.StartsWith('-'))
            return "diff-del";
        return "";
    }

    private async Task<HashSet<string>> GetReloadableFilesAsync()
    {
        if (gameRpc.Connected)
        {
            try
            {
                var remote = await gameRpc.CallAsync<string[]>("ReloadableDataFiles");
                return new HashSet<string>(remote, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // Fall back to the compile-time list
            }
        }
        return new HashSet<string>(DataReload.Files, StringComparer.OrdinalIgnoreCase);
    }
}
