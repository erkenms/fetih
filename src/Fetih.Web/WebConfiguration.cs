namespace Fetih.Web;

using Fetih.Core.Logging;

public class WebConfiguration
{
    public DatabaseConfiguration? Database { get; set; }
    public DatabaseConfiguration? AccountDatabase { get; set; }
    public WebSection Web { get; set; } = new();
    public GameRpcSection GameRpc { get; set; } = new();
    public PathsSection Paths { get; set; } = new();
    public LoggingConfiguration Logging { get; set; } = new();

    public class DatabaseConfiguration
    {
        public string Hostname { get; set; } = "";
        public string Schema { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";

        public string ConnectionString =>
            $"server={this.Hostname};database={this.Schema};user={this.Username};password={this.Password}";
    }

    public class WebSection
    {
        public string Urls { get; set; } = "http://127.0.0.1:5820";

        // On when the site is served over HTTPS, so the session cookie is never sent in clear
        public bool SecureCookies { get; set; }

        // Per IP address
        public int LoginAttemptsPerMinute { get; set; } = 10;

        // On behind a reverse proxy: the client's address and scheme come from its
        // X-Forwarded-For and X-Forwarded-Proto headers. Any sender is trusted, so only
        // when nothing but the proxy can reach the web.
        public bool ForwardedHeaders { get; set; }

        // Only Moderator (Authority >= 2) and above may sign in or register
        public bool StaffOnly { get; set; }

        // Disables Studio writes and reloads (used on production, where deploys own the checkout)
        public bool StudioReadOnly { get; set; }
    }

    public class GameRpcSection
    {
        public string Hostname { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 5817;
    }

    // Relative paths are from the build output folder, like the game server's Maps:ClientDirectory
    public class PathsSection
    {
        public string? DataDirectory { get; set; }
        public string? AssetsDirectory { get; set; }
        public string? ClientDirectory { get; set; }
        public string? FrontendDirectory { get; set; }

        public static string? Resolve(string? path) =>
            string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path, AppContext.BaseDirectory);
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(Database), nameof(AccountDatabase))]
    public bool Valid => this.Database != null && this.AccountDatabase != null;
}
