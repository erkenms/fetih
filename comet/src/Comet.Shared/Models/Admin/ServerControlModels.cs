namespace Comet.Shared.Models.Admin
{
    using System;

    public enum ShutdownMode
    {
        Shutdown,
        Restart,
    }

    public class ServerControlState
    {
        public bool MaintenanceEnabled { get; set; }
        public string MaintenanceMessage { get; set; } = string.Empty;
        public string Motd { get; set; } = string.Empty;
        public bool ShutdownPending { get; set; }
        public ShutdownMode? ShutdownMode { get; set; }
        public DateTime? ShutdownAt { get; set; }
        public string ShutdownReason { get; set; } = string.Empty;
        public int OnlinePlayers { get; set; }
        public int OnlineNonGmPlayers { get; set; }
    }

    public class ShutdownArgs : AdminArgs
    {
        public ShutdownMode Mode { get; set; }
        public int Seconds { get; set; }
        public string Reason { get; set; }
    }

    public class MaintenanceArgs : AdminArgs
    {
        public bool Enabled { get; set; }
        public string Message { get; set; }
        public bool KickNonGm { get; set; }
    }

    public class MotdArgs : AdminArgs
    {
        public string Text { get; set; }
    }
}
