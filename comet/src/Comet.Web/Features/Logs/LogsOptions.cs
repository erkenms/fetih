namespace Comet.Web.Features.Logs;

public sealed class LogsOptions
{
    public string LokiUrl { get; set; } = "http://127.0.0.1:3100";
    public string GrafanaUrl { get; set; } = "http://localhost:3000";
}
