namespace Comet.Game
{
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Admin;
    using Comet.Shared.Models.Admin;

    public partial class Remote
    {
        public Task<ServerControlState> ServerControlStatus() =>
            Task.FromResult(ServerControl.Snapshot());

        // Comet.Account asks before letting a player's login through
        public Task<bool> IsMaintenance() =>
            Task.FromResult(ServerControl.MaintenanceEnabled);

        public async Task<AdminResult> StartShutdown(ShutdownArgs args)
        {
            var result = await ServerControl.StartShutdownAsync(args.Mode, args.Seconds, args.Reason);
            LogEvents.For<Remote>(LogEvents.GmShutdown).Information(
                "Admin account {ActorAccountId} scheduled {Mode} in {Seconds}s ({Reason}): {Result}",
                args.ActorAccountID, args.Mode, args.Seconds, args.Reason ?? "", ResultText(result));
            return result;
        }

        public async Task<AdminResult> CancelShutdown(AdminArgs args)
        {
            var result = await ServerControl.CancelShutdownAsync();
            LogEvents.For<Remote>(LogEvents.GmShutdownCancel).Information(
                "Admin account {ActorAccountId} cancelled scheduled shutdown: {Result}",
                args.ActorAccountID, ResultText(result));
            return result;
        }

        public async Task<AdminResult> SetMaintenance(MaintenanceArgs args)
        {
            var result = await ServerControl.SetMaintenanceAsync(args.Enabled, args.Message, args.KickNonGm);
            LogEvents.For<Remote>(LogEvents.GmMaintenance).Information(
                "Admin account {ActorAccountId} set maintenance={Enabled} kick={Kick} ({Message}): {Result}",
                args.ActorAccountID, args.Enabled, args.KickNonGm, args.Message ?? "", ResultText(result));
            return result;
        }

        public Task<AdminResult> SetMotd(MotdArgs args)
        {
            var result = ServerControl.SetMotd(args.Text);
            LogEvents.For<Remote>(LogEvents.GmMotd).Information(
                "Admin account {ActorAccountId} updated MOTD ({Length} chars): {Result}",
                args.ActorAccountID, (args.Text ?? "").Trim().Length, ResultText(result));
            return Task.FromResult(result);
        }
    }
}
