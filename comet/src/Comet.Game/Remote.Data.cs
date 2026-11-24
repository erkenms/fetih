namespace Comet.Game
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Shared.Models;

    public partial class Remote
    {
        // A Data/ file by name, as ReloadableDataFiles lists them
        public Task<DataReloadResult> ReloadData(string file) => DataReload.ReloadAsync(file);

        public IReadOnlyList<string> ReloadableDataFiles() => DataReload.Files;
    }
}
