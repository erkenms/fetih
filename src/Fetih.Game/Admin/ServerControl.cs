namespace Fetih.Game.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Chat;
    using Fetih.Game.Database;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Network.Sockets;
    using Fetih.Shared.Models.Admin;
    using Microsoft.EntityFrameworkCore;

    public static class ServerControl
    {
        public const int MinCountdownSeconds = 1;
        public const int MaxCountdownSeconds = 7200;
        public const int MaxMaintenanceMessageLength = 255;
        public const int MaxMotdLength = 1000;
        public const int MaxReasonLength = 200;
        public const string DefaultMaintenanceMessage =
            "The server is currently under maintenance. Please try again later.";

        private static readonly int[] StandardMilestones =
            [3600, 1800, 900, 600, 300, 180, 120, 60, 30, 10, 5, 4, 3, 2, 1];

        private static readonly object Sync = new();

        private static bool maintenanceEnabled;
        private static string maintenanceMessage = string.Empty;
        private static string motd = string.Empty;

        private static CancellationTokenSource shutdownCts;
        private static ShutdownMode? shutdownMode;
        private static DateTime? shutdownAt;
        private static string shutdownReason = string.Empty;

        public static Action<ShutdownMode> RequestShutdown { get; set; }
        public static Task ActiveCountdownTask { get; private set; }

        public static bool MaintenanceEnabled
        {
            get { lock (Sync) return maintenanceEnabled; }
        }

        public static string MaintenanceMessage
        {
            get { lock (Sync) return maintenanceMessage; }
        }

        public static string EffectiveMaintenanceMessage
        {
            get
            {
                lock (Sync)
                    return string.IsNullOrWhiteSpace(maintenanceMessage)
                        ? DefaultMaintenanceMessage
                        : maintenanceMessage;
            }
        }

        public static string Motd
        {
            get { lock (Sync) return motd; }
        }

        public static bool IsAllowedDuringMaintenance(ushort authorityID) =>
            !MaintenanceEnabled || authorityID > (ushort)Authority.Player;

        public static ServerControlState Snapshot()
        {
            var players = Kernel.Players.Values.Where(p => p.Character != null).ToList();
            int nonGm = players.Count(p => p.Authority <= Authority.Player);
            lock (Sync)
            {
                return new ServerControlState
                {
                    MaintenanceEnabled = maintenanceEnabled,
                    MaintenanceMessage = maintenanceMessage,
                    Motd = motd,
                    ShutdownPending = shutdownCts != null,
                    ShutdownMode = shutdownMode,
                    ShutdownAt = shutdownAt,
                    ShutdownReason = shutdownReason,
                    OnlinePlayers = players.Count,
                    OnlineNonGmPlayers = nonGm,
                };
            }
        }

        public static async Task LoadAsync()
        {
            try
            {
                await using var db = new ServerDbContext();
                var row = await db.ServerSettings.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.ID == DbServerSettings.SingletonId);
                if (row == null)
                    return;
                lock (Sync)
                {
                    maintenanceEnabled = row.MaintenanceEnabled;
                    maintenanceMessage = row.MaintenanceMessage ?? string.Empty;
                    motd = row.Motd ?? string.Empty;
                }
            }
            catch (Exception e)
            {
                LogEvents.For(typeof(ServerControl), LogEvents.ServerStart).Warning(
                    e, "Could not load web_server_settings; using defaults");
            }
        }

        public static async Task SendMotdAsync(Client client)
        {
            string text = Motd;
            if (client?.Character == null || string.IsNullOrWhiteSpace(text))
                return;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length > 0)
                    await Talk.NoticeAsync(client, line);
            }
        }

        public static IReadOnlyList<int> CountdownMilestones(int totalSeconds) =>
            StandardMilestones.Where(m => m < totalSeconds).ToArray();

        public static string FormatRemaining(int seconds)
        {
            if (seconds >= 3600 && seconds % 3600 == 0)
            {
                int hours = seconds / 3600;
                return hours == 1 ? "1 hour" : $"{hours} hours";
            }
            if (seconds >= 60 && seconds % 60 == 0)
            {
                int minutes = seconds / 60;
                return minutes == 1 ? "1 minute" : $"{minutes} minutes";
            }
            return seconds == 1 ? "1 second" : $"{seconds} seconds";
        }

        public static string FormatCountdownMessage(ShutdownMode mode, int remainingSeconds, string reason)
        {
            string verb = mode == ShutdownMode.Restart ? "restart" : "shut down";
            string progressive = mode == ShutdownMode.Restart ? "restarting" : "shutting down";
            string baseText = remainingSeconds > 0
                ? $"Server will {verb} in {FormatRemaining(remainingSeconds)}."
                : $"Server is {progressive} now.";
            return string.IsNullOrWhiteSpace(reason) ? baseText : $"{baseText} ({reason.Trim()})";
        }

        public static string FormatCancelMessage(ShutdownMode mode) =>
            mode == ShutdownMode.Restart
                ? "Server restart has been cancelled."
                : "Server shutdown has been cancelled.";

        public static async Task<AdminResult> StartShutdownAsync(
            ShutdownMode mode,
            int seconds,
            string reason,
            Func<string, Task> broadcast = null,
            Action<ShutdownMode> onTrigger = null,
            Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            if (seconds < MinCountdownSeconds || seconds > MaxCountdownSeconds)
                return AdminResult.Fail(AdminErrors.OutOfRange);

            string cleanReason = (reason ?? string.Empty).Trim();
            if (cleanReason.Length > MaxReasonLength)
                cleanReason = cleanReason[..MaxReasonLength];

            CancellationTokenSource cts;
            lock (Sync)
            {
                if (shutdownCts != null)
                    return AdminResult.Fail(AdminErrors.Busy);
                cts = new CancellationTokenSource();
                shutdownCts = cts;
                shutdownMode = mode;
                shutdownAt = DateTime.UtcNow.AddSeconds(seconds);
                shutdownReason = cleanReason;
            }

            broadcast ??= text => Talk.BroadcastAsync(MsgTalk.TalkChannel.Center, text);
            onTrigger ??= m => RequestShutdown?.Invoke(m);
            delay ??= (span, ct) => Task.Delay(span, ct);

            await broadcast(FormatCountdownMessage(mode, seconds, cleanReason));

            var task = RunCountdownAsync(mode, seconds, cleanReason, cts, broadcast, onTrigger, delay);
            ActiveCountdownTask = task;
            return AdminResult.Done(true);
        }

        private static async Task RunCountdownAsync(
            ShutdownMode mode,
            int seconds,
            string reason,
            CancellationTokenSource cts,
            Func<string, Task> broadcast,
            Action<ShutdownMode> onTrigger,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            var token = cts.Token;
            try
            {
                int current = seconds;
                foreach (int milestone in CountdownMilestones(seconds))
                {
                    await delay(TimeSpan.FromSeconds(current - milestone), token);
                    await broadcast(FormatCountdownMessage(mode, milestone, reason));
                    current = milestone;
                }

                await delay(TimeSpan.FromSeconds(current), token);
                await broadcast(FormatCountdownMessage(mode, 0, reason));
                // Give sockets a moment to flush the final warning before closing connections.
                await delay(TimeSpan.FromMilliseconds(250), CancellationToken.None);

                lock (Sync)
                {
                    if (ReferenceEquals(shutdownCts, cts))
                    {
                        shutdownCts = null;
                        shutdownMode = null;
                        shutdownAt = null;
                        shutdownReason = string.Empty;
                    }
                }

                onTrigger(mode);
            }
            catch (OperationCanceledException)
            {
                // Cancelled via CancelShutdownAsync
            }
            catch (Exception e)
            {
                LogEvents.For(typeof(ServerControl), LogEvents.ServerShutdown).Error(
                    e, "Shutdown countdown failed");
                lock (Sync)
                {
                    if (ReferenceEquals(shutdownCts, cts))
                    {
                        shutdownCts = null;
                        shutdownMode = null;
                        shutdownAt = null;
                        shutdownReason = string.Empty;
                    }
                }
            }
            finally
            {
                cts.Dispose();
            }
        }

        public static async Task<AdminResult> CancelShutdownAsync(Func<string, Task> broadcast = null)
        {
            CancellationTokenSource cts;
            ShutdownMode mode;
            lock (Sync)
            {
                if (shutdownCts == null || shutdownMode == null)
                    return AdminResult.Fail(AdminErrors.NotFound);
                cts = shutdownCts;
                mode = shutdownMode.Value;
                shutdownCts = null;
                shutdownMode = null;
                shutdownAt = null;
                shutdownReason = string.Empty;
            }

            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }

            broadcast ??= text => Talk.BroadcastAsync(MsgTalk.TalkChannel.Center, text);
            await broadcast(FormatCancelMessage(mode));
            return AdminResult.Done(true);
        }

        // The web saves the settings to web_server_settings; the game reads them at startup
        public static async Task<AdminResult> SetMaintenanceAsync(bool enabled, string message, bool kickNonGm)
        {
            string clean = (message ?? string.Empty).Trim();
            if (clean.Length > MaxMaintenanceMessageLength)
                return AdminResult.Fail(AdminErrors.Invalid);

            lock (Sync)
            {
                maintenanceEnabled = enabled;
                maintenanceMessage = clean;
            }

            if (enabled && kickNonGm)
            {
                string notice = EffectiveMaintenanceMessage;
                foreach (var player in Kernel.Players.Values.ToList())
                {
                    if (player.Character == null || player.Authority > Authority.Player)
                        continue;
                    await Talk.NoticeAsync(player, notice);
                    player.Disconnect(DisconnectReasons.Kicked);
                }
            }

            return AdminResult.Done(true);
        }

        public static AdminResult SetMotd(string text)
        {
            string clean = (text ?? string.Empty).Trim();
            if (clean.Length > MaxMotdLength)
                return AdminResult.Fail(AdminErrors.Invalid);

            lock (Sync)
            {
                motd = clean;
            }
            return AdminResult.Done(true);
        }

        public static void ResetForTests()
        {
            lock (Sync)
            {
                try { shutdownCts?.Cancel(); } catch (ObjectDisposedException) { }
                shutdownCts = null;
                shutdownMode = null;
                shutdownAt = null;
                shutdownReason = string.Empty;
                maintenanceEnabled = false;
                maintenanceMessage = string.Empty;
                motd = string.Empty;
            }
            RequestShutdown = null;
            ActiveCountdownTask = null;
        }
    }
}
