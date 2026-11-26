namespace Fetih.Network.Services
{
    using System;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Hosting;
    using Org.BouncyCastle.Math;

    public class PrimeGeneratorService : BackgroundService
    {
        private int BitLength;
        private Channel<BigInteger> BufferChannel;
        protected Random Generator;

        public PrimeGeneratorService(int capacity = 100, int bitLength = 256)
        {
            this.BitLength = bitLength;
            this.BufferChannel = Channel.CreateBounded<BigInteger>(capacity);
            this.Generator = new Random();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await this.BufferChannel.Writer.WriteAsync(
                    BigInteger.ProbablePrime(this.BitLength, this.Generator), 
                    stoppingToken);
            }
        }

        public Task<BigInteger> NextAsync() 
        {
            return this.BufferChannel.Reader.ReadAsync().AsTask();
        } 
    }
}
