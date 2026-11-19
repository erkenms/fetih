namespace Comet.Game.States
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;

    // Saves every online character now and then, so a crash loses at most one interval
    public static class SaveLoop
    {
        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try { await SaveAllAsync(); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(SaveLoop), LogEvents.CharacterSaveFailed).Error(e,
                        "Periodic save round failed");
                }
            }
        }

        public static async Task SaveAllAsync()
        {
            var watch = Stopwatch.StartNew();
            var players = Kernel.Players.Values.ToList();
            if (players.Count == 0)
                return;
            var results = await Task.WhenAll(players.Select(p => p.SaveAsync("periodic")));
            int saved = results.Count(r => r);
            LogEvents.For(typeof(SaveLoop), LogEvents.CharacterSaveRound).Information(
                "Saved {Saved} of {Online} online characters in {DurationMs} ms",
                saved, players.Count, watch.ElapsedMilliseconds);
        }
    }
}
