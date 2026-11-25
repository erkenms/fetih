namespace Comet.Game.Tests.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Game.Admin;
    using Comet.Game.States;
    using Comet.Shared.Models.Admin;
    using Xunit;

    [Collection("ServerControl")]
    public class ServerControlTests : IDisposable
    {
        public ServerControlTests() => ServerControl.ResetForTests();

        public void Dispose() => ServerControl.ResetForTests();

        [Fact]
        public void CountdownMilestonesMatchExpectedSchedule()
        {
            Assert.Equal([300, 180, 120, 60, 30, 10, 5, 4, 3, 2, 1], ServerControl.CountdownMilestones(600));
            Assert.Equal([30, 10, 5, 4, 3, 2, 1], ServerControl.CountdownMilestones(60));
            Assert.Equal([4, 3, 2, 1], ServerControl.CountdownMilestones(5));
            Assert.Empty(ServerControl.CountdownMilestones(1));
        }

        [Fact]
        public void FormatCountdownMessageCoversRestartShutdownAndReason()
        {
            Assert.Equal("Server will restart in 5 minutes.",
                ServerControl.FormatCountdownMessage(ShutdownMode.Restart, 300, ""));
            Assert.Equal("Server will shut down in 1 minute. (Patch)",
                ServerControl.FormatCountdownMessage(ShutdownMode.Shutdown, 60, "Patch"));
            Assert.Equal("Server is restarting now.",
                ServerControl.FormatCountdownMessage(ShutdownMode.Restart, 0, ""));
            Assert.Equal("Server is shutting down now. (Maintenance)",
                ServerControl.FormatCountdownMessage(ShutdownMode.Shutdown, 0, "Maintenance"));
        }

        [Fact]
        public async Task StartShutdownBroadcastsMilestonesAndTriggersCallback()
        {
            var messages = new List<string>();
            ShutdownMode? triggered = null;

            var result = await ServerControl.StartShutdownAsync(
                ShutdownMode.Restart,
                seconds: 60,
                reason: "Update",
                broadcast: text => { messages.Add(text); return Task.CompletedTask; },
                onTrigger: mode => triggered = mode,
                delay: (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });

            Assert.True(result.Ok);
            await ServerControl.ActiveCountdownTask;

            Assert.Equal(ShutdownMode.Restart, triggered);
            Assert.False(ServerControl.Snapshot().ShutdownPending);
            Assert.Equal("Server will restart in 1 minute. (Update)", messages[0]);
            Assert.Contains("Server will restart in 30 seconds. (Update)", messages);
            Assert.Contains("Server will restart in 10 seconds. (Update)", messages);
            Assert.Contains("Server will restart in 1 second. (Update)", messages);
            Assert.Equal("Server is restarting now. (Update)", messages[^1]);
        }

        [Fact]
        public async Task StartShutdownRejectsDuplicateAndCancelStopsCountdown()
        {
            var messages = new List<string>();
            bool triggered = false;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var first = await ServerControl.StartShutdownAsync(
                ShutdownMode.Shutdown,
                seconds: 120,
                reason: "",
                broadcast: text => { messages.Add(text); return Task.CompletedTask; },
                onTrigger: _ => triggered = true,
                delay: async (_, ct) => await gate.Task.WaitAsync(ct));

            Assert.True(first.Ok);
            Assert.True(ServerControl.Snapshot().ShutdownPending);

            var second = await ServerControl.StartShutdownAsync(
                ShutdownMode.Restart,
                seconds: 60,
                reason: "",
                broadcast: _ => Task.CompletedTask,
                onTrigger: _ => { },
                delay: (_, _) => Task.CompletedTask);

            Assert.False(second.Ok);
            Assert.Equal(AdminErrors.Busy, second.Error);

            var cancelled = await ServerControl.CancelShutdownAsync(
                text => { messages.Add(text); return Task.CompletedTask; });
            Assert.True(cancelled.Ok);

            await ServerControl.ActiveCountdownTask;
            Assert.False(triggered);
            Assert.False(ServerControl.Snapshot().ShutdownPending);
            Assert.Equal("Server shutdown has been cancelled.", messages[^1]);

            var cancelAgain = await ServerControl.CancelShutdownAsync(_ => Task.CompletedTask);
            Assert.False(cancelAgain.Ok);
            Assert.Equal(AdminErrors.NotFound, cancelAgain.Error);
        }

        [Fact]
        public async Task MaintenanceAllowsOnlyAuthorityAbovePlayer()
        {
            Assert.True(ServerControl.IsAllowedDuringMaintenance((ushort)Authority.Player));

            var enabled = await ServerControl.SetMaintenanceAsync(true, "", false);
            Assert.True(enabled.Ok);
            Assert.Equal(ServerControl.DefaultMaintenanceMessage, ServerControl.EffectiveMaintenanceMessage);
            Assert.False(ServerControl.IsAllowedDuringMaintenance((ushort)Authority.Player));
            Assert.True(ServerControl.IsAllowedDuringMaintenance((ushort)Authority.Moderator));
            Assert.True(ServerControl.IsAllowedDuringMaintenance((ushort)Authority.GameMaster));
            Assert.True(ServerControl.IsAllowedDuringMaintenance((ushort)Authority.Administrator));

            await ServerControl.SetMaintenanceAsync(true, "Back at 18:00 UTC", false);
            Assert.Equal("Back at 18:00 UTC", ServerControl.EffectiveMaintenanceMessage);

            await ServerControl.SetMaintenanceAsync(false, "", false);
            Assert.True(ServerControl.IsAllowedDuringMaintenance((ushort)Authority.Player));
        }

        [Fact]
        public void MotdTrimsAndValidatesLength()
        {
            var ok = ServerControl.SetMotd("  Welcome to Fetih!  ");
            Assert.True(ok.Ok);
            Assert.Equal("Welcome to Fetih!", ServerControl.Motd);

            var tooLong = ServerControl.SetMotd(new string('a', ServerControl.MaxMotdLength + 1));
            Assert.False(tooLong.Ok);
            Assert.Equal(AdminErrors.Invalid, tooLong.Error);
        }
    }
}
