namespace Comet.Web.Features.Integrity;

public sealed record IntegrityProblem(
    string File,
    string Category,
    string Level,
    int? Entry,
    int? Line,
    string? EntityId,
    string Message,
    string EditorPath,
    string EditorKind);

public sealed record IntegrityFileSummary(
    string File,
    int Errors,
    int Warnings,
    int CheckedEntries);

public sealed record IntegrityReportResponse(
    string DataDirectory,
    bool ClientFilesAvailable,
    int ErrorCount,
    int WarningCount,
    int CheckedFiles,
    IntegrityFileSummary[] Files,
    IntegrityProblem[] Problems);
