namespace Fetih.Game.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Database.Repositories;
    using Fetih.Game.States;

    // Changes from outside the game (the admin RPC) to a character, online or not. Online, the
    // change runs on the player's own packet queue; offline, on the saved character, while
    // logins for the account wait.
    public static class CharacterAccess
    {
        public static TimeSpan QueueTimeout = TimeSpan.FromSeconds(10);

        public enum Outcome { Done, NotFound, Busy }

        // Ran is false if the player left before the work's turn came
        public static async Task<(bool Ran, T Result)> OnQueueAsync<T>(Client client, Func<Task<T>> work)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            int state = 0; // 1 started, 2 given up
            Kernel.Post(client, async () =>
            {
                if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
                    return;
                try { done.TrySetResult(await work()); }
                catch (Exception e) { done.TrySetException(e); }
            });
            var first = await Task.WhenAny(done.Task, Task.Delay(QueueTimeout));
            if (first != done.Task && Interlocked.CompareExchange(ref state, 2, 0) == 0)
                return (false, default);
            return (true, await done.Task);
        }

        public static async Task<(Outcome Outcome, bool Online, T Result)> RunAsync<T>(uint characterID,
            Func<Client, Task<T>> online, Func<DbCharacter, Task<T>> offline)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (Kernel.Players.TryGet(characterID, out var player) && player.Character != null)
                {
                    var (ran, result) = await OnQueueAsync(player, () => online(player));
                    if (ran)
                        return (Outcome.Done, true, result);
                    continue;
                }

                var saved = await CharactersRepository.FindByIdAsync(characterID);
                if (saved == null)
                    return (Outcome.NotFound, false, default);

                // A login checks Kernel.Logouts after it joins Kernel.Clients; this registers
                // first and checks after, so either the login waits or this sees it
                uint accountID = saved.AccountID;
                var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!Kernel.Logouts.TryAdd(accountID, hold.Task))
                {
                    if (Kernel.Logouts.TryGetValue(accountID, out var saving))
                        await saving;
                    continue;
                }
                try
                {
                    if (Kernel.Clients.TryGet(accountID, out var session))
                    {
                        if (session.Character == null)
                            return (Outcome.Busy, false, default);
                        continue;
                    }
                    saved = await CharactersRepository.FindByIdAsync(characterID);
                    if (saved == null)
                        return (Outcome.NotFound, false, default);
                    return (Outcome.Done, false, await offline(saved));
                }
                finally
                {
                    Kernel.Logouts.TryRemove(new KeyValuePair<uint, Task>(accountID, hold.Task));
                    hold.SetResult();
                }
            }
            return (Outcome.Busy, false, default);
        }
    }
}
