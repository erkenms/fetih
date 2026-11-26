namespace Fetih.Web;

using Fetih.Shared.Models;
using Fetih.Web.Audit;
using Fetih.Web.Game;

// Where the Studio pages find the game's data files
public static class StudioData
{
    public static async Task<DataReloadResult> ReloadFileAsync(string fileName, GameRpc gameRpc, AuditLog audit)
    {
        var result = await gameRpc.CallAsync<DataReloadResult>("ReloadData", fileName);
        await audit.WriteAsync(
            "data.reload",
            "data_file",
            fileName,
            after: result,
            summary: result.Applied ? result.Summary : string.Join("; ", result.Errors));
        return result;
    }
    // The configured directory, else the checkout's src/Fetih.Game/Data above the binaries
    public static string ResolveDataDirectory(WebConfiguration config)
    {
        string? configured = WebConfiguration.PathsSection.Resolve(config.Paths.DataDirectory);
        if (configured != null && Directory.Exists(configured))
            return configured;

        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "src", "Fetih.Game", "Data");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return configured ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Fetih.Game/Data"));
    }
}
