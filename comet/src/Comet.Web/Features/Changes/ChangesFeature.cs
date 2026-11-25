namespace Comet.Web.Features.Changes;

using Comet.Game;
using Comet.Game.DataEditing;
using Comet.Shared.Models;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Comet.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;

public class ChangesFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<GitDataService>();
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/changes").RequirePermission(Permission.Studio);
        group.MapGet("/", GetChangesAsync).WithName("getChanges");
        group.MapGet("/{file}/diff", GetDiffAsync).WithName("getChangeDiff");
        group.MapPost("/{file}/reload", ReloadAsync).WithName("reloadDataFile");
        group.MapPost("/{file}/discard", DiscardAsync).WithName("discardDataFile");
    }

    private static async Task<Ok<ChangesSummaryResponse>> GetChangesAsync(
        WebConfiguration config, GitDataService git, GameRpc gameRpc, CancellationToken ct)
    {
        string dataDir = StudioData.ResolveDataDirectory(config);
        var (gitAvailable, statusByFile) = Directory.Exists(dataDir)
            ? await git.GetStatusAsync(dataDir, ct)
            : (false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var problems = Directory.Exists(dataDir)
            ? ValidateAll(dataDir, config)
            : [];
        var problemsByFile = problems
            .GroupBy(p => p.File, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

        var reloadable = await GetReloadableFilesAsync(gameRpc);

        var files = new List<DataFileChange>();
        foreach (var kind in DataFileKinds.All)
        {
            bool exists = Directory.Exists(dataDir) && File.Exists(Path.Combine(dataDir, kind.FileName));
            string gitStatus = statusByFile.TryGetValue(kind.FileName, out var s) ? s : "clean";
            if (!exists && gitStatus == "clean")
                continue;

            int added = 0;
            int removed = 0;
            if (gitStatus != "clean")
            {
                string diff = await git.GetDiffAsync(dataDir, kind.FileName, gitStatus, ct);
                (added, removed) = GitDataService.CountDiffLines(diff);
            }

            files.Add(new DataFileChange(
                kind.FileName,
                gitStatus,
                added,
                removed,
                reloadable.Contains(kind.FileName),
                problemsByFile.TryGetValue(kind.FileName, out var fileProblems) ? fileProblems : []));
        }

        return TypedResults.Ok(new ChangesSummaryResponse(
            gitAvailable, gameRpc.Connected, dataDir, [.. files], problems));
    }

    private static async Task<Results<Ok<FileDiffResponse>, NotFound<ApiError>>> GetDiffAsync(
        string file, WebConfiguration config, GitDataService git, CancellationToken ct)
    {
        var kind = DataFileKinds.Find(file);
        if (kind == null)
            return TypedResults.NotFound(new ApiError("not_found"));

        string dataDir = StudioData.ResolveDataDirectory(config);
        var (_, statusByFile) = Directory.Exists(dataDir)
            ? await git.GetStatusAsync(dataDir, ct)
            : (false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        string gitStatus = statusByFile.TryGetValue(kind.FileName, out var s) ? s : "clean";
        string diff = gitStatus == "clean" || !Directory.Exists(dataDir)
            ? ""
            : await git.GetDiffAsync(dataDir, kind.FileName, gitStatus, ct);
        var problems = Directory.Exists(dataDir) ? ValidateFile(dataDir, config, kind.FileName) : [];

        return TypedResults.Ok(new FileDiffResponse(kind.FileName, gitStatus, diff, problems));
    }

    private static async Task<Results<Ok<ReloadResponse>, NotFound<ApiError>, BadRequest<ApiError>>> ReloadAsync(
        string file, GameRpc gameRpc, AuditLog audit)
    {
        var kind = DataFileKinds.Find(file);
        if (kind == null)
            return TypedResults.NotFound(new ApiError("not_found"));

        if (!DataReload.Files.Contains(kind.FileName, StringComparer.OrdinalIgnoreCase))
            return TypedResults.BadRequest(new ApiError("not_reloadable"));

        var result = await gameRpc.CallAsync<DataReloadResult>("ReloadData", kind.FileName);
        await audit.WriteAsync(
            "data.reload",
            "data_file",
            kind.FileName,
            after: result,
            summary: result.Applied ? result.Summary : string.Join("; ", result.Errors));

        return TypedResults.Ok(new ReloadResponse(
            result.File ?? kind.FileName,
            result.Applied,
            [.. result.Errors],
            [.. result.Warnings],
            result.Summary));
    }

    private static async Task<Results<Ok<DiscardResponse>, NotFound<ApiError>>> DiscardAsync(
        string file, WebConfiguration config, GitDataService git, AuditLog audit, CancellationToken ct)
    {
        var kind = DataFileKinds.Find(file);
        if (kind == null)
            return TypedResults.NotFound(new ApiError("not_found"));

        string dataDir = StudioData.ResolveDataDirectory(config);
        var (_, statusByFile) = Directory.Exists(dataDir)
            ? await git.GetStatusAsync(dataDir, ct)
            : (false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        string gitStatus = statusByFile.TryGetValue(kind.FileName, out var s) ? s : "clean";
        string diff = gitStatus == "clean" ? "" : await git.GetDiffAsync(dataDir, kind.FileName, gitStatus, ct);

        bool discarded = await git.DiscardAsync(dataDir, kind.FileName, ct);
        if (discarded)
        {
            await audit.WriteAsync(
                "data.discard",
                "data_file",
                kind.FileName,
                before: string.IsNullOrEmpty(diff) ? null : new { diff },
                summary: $"Discarded local changes in {kind.FileName}");
        }

        return TypedResults.Ok(new DiscardResponse(kind.FileName, discarded));
    }

    private static ValidationProblemDto[] ValidateAll(string dataDir, WebConfiguration config) =>
        StudioData.CreateDataSet(dataDir, config).ValidateAll().Select(ToDto).ToArray();

    private static ValidationProblemDto[] ValidateFile(string dataDir, WebConfiguration config, string fileName) =>
        StudioData.CreateDataSet(dataDir, config).Validate(fileName).Select(ToDto).ToArray();

    private static ValidationProblemDto ToDto(DataProblem p) =>
        new(p.File, p.Entry, p.Line, p.Message, p.Level == DataProblemLevel.Warning ? "warning" : "error");

    private static async Task<HashSet<string>> GetReloadableFilesAsync(GameRpc gameRpc)
    {
        if (gameRpc.Connected)
        {
            try
            {
                var remote = await gameRpc.CallAsync<string[]>("ReloadableDataFiles");
                return new HashSet<string>(remote, StringComparer.OrdinalIgnoreCase);
            }
            catch { /* Fall back to the shared compile-time list */ }
        }
        return new HashSet<string>(DataReload.Files, StringComparer.OrdinalIgnoreCase);
    }
}
