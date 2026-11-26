namespace Fetih.Account
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Account.Database.Models;
    using Fetih.Network.Services;

    public static class Kernel
    {
        public static Dictionary<string, DbRealm> Realms;

        public static class Services
        {
            public static RandomnessService Randomness = new RandomnessService();
        }
        
        public static Task<int> NextAsync(int minValue, int maxValue) => 
            Services.Randomness.NextAsync(minValue, maxValue);
    }
}
