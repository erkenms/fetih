namespace Fetih.Game
{
    using System;
    using System.Runtime.Caching;
    using System.Security.Cryptography;
    using Fetih.Core.Logging;
    using Fetih.Network.RPC;
    using Fetih.Shared.Models;

    public partial class Remote : IRpcServerTarget
    {
        public void Connected(string agentName)
        {
            LogEvents.For<Remote>(LogEvents.RpcConnected).Information(
                "RPC client {Agent} connected", agentName);
        }

        public ulong TransferAuth(TransferAuthArgs args)
        {
            // The client receives the token as two halves and reads a zero low half as a
            // rejection, so avoid that.
            ulong token;
            do token = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
            while ((uint)token == 0);

            var timeoutPolicy = new CacheItemPolicy();
            timeoutPolicy.AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(10);
            Kernel.Logins.Set(token.ToString(), args, timeoutPolicy) ;
            return token;
        }
    }
}
