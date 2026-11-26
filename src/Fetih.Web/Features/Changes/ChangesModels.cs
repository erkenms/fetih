namespace Fetih.Web.Features.Changes;

public record ValidationProblemDto(string File, int? Entry, int? Line, string Message, string Level);

public record DataFileChange(
    string File,
    string GitStatus,
    int AddedLines,
    int RemovedLines,
    bool Reloadable,
    ValidationProblemDto[] Problems);

public record ChangesSummaryResponse(
    bool GitAvailable,
    bool GameConnected,
    string DataDirectory,
    DataFileChange[] Files,
    ValidationProblemDto[] AllProblems);

public record FileDiffResponse(
    string File,
    string GitStatus,
    string Diff,
    ValidationProblemDto[] Problems);

public record ReloadResponse(
    string File,
    bool Applied,
    string[] Errors,
    string[] Warnings,
    string? Summary);

public record DiscardResponse(string File, bool Discarded);
