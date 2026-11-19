namespace Comet.Account
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Account.Database.Models;
    using Comet.Network.Services;

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
