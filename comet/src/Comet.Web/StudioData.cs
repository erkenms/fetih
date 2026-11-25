namespace Comet.Web;

using Comet.Game.DataEditing;
using Comet.Shared.Models;
using Comet.Web.Audit;
using Comet.Web.Game;

// Where the Studio pages find the game's data files, and the validation set over them
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
    // The configured directory, else the checkout's comet/src/Comet.Game/Data above the binaries
    public static string ResolveDataDirectory(WebConfiguration config)
    {
        string? configured = WebConfiguration.PathsSection.Resolve(config.Paths.DataDirectory);
        if (configured != null && Directory.Exists(configured))
            return configured;

        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "src", "Comet.Game", "Data");
            if (Directory.Exists(candidate))
                return candidate;
            candidate = Path.Combine(dir, "comet", "src", "Comet.Game", "Data");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return configured ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Comet.Game/Data"));
    }

    // Checks against the client's maps and items when its files are there, else the files alone
    public static DataSet CreateDataSet(string dataDir, WebConfiguration config)
    {
        string? clientDir = WebConfiguration.PathsSection.Resolve(config.Paths.ClientDirectory);
        if (clientDir != null
            && File.Exists(Path.Combine(clientDir, "ini", "GameMap.dat"))
            && File.Exists(Path.Combine(clientDir, "ini", "itemtype.dat")))
        {
            try { return DataSet.ForClient(dataDir, clientDir); }
            catch { /* Fall back to schema-only checks when client data is incomplete */ }
        }
        return new DataSet(dataDir);
    }
}
