namespace Fetih.Account.Database.Repositories 
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Account.Database;
    using Fetih.Account.Database.Models;
    using Fetih.Network.RPC;
    using Microsoft.EntityFrameworkCore;

    public static class RealmsRepository
    {
        public static async Task LoadAsync()
        {
            using var db = new ServerDbContext();
            Kernel.Realms = await db.Realms
                .Include(x => x.Authority)
                .ToDictionaryAsync(x => x.Name);

            foreach (var realm in Kernel.Realms.Values)
            {
                realm.Rpc = new RpcClient();
                var task = realm.Rpc.ConnectAsync(
                    realm.RpcIPAddress, (int)realm.RpcPort, "Account Server");
            }
        }
    }
}
