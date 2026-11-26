namespace Fetih.Core.Logging
{
    using System;
    using System.Collections.Generic;
    using Serilog.Events;

    // Allowlist of events copied to the permanent, never-deleted audit file.
    public static class AuditEvents
    {
        private static readonly HashSet<string> Exact = new HashSet<string>(StringComparer.Ordinal)
        {
            LogEvents.AccountLogin,
            LogEvents.AccountLoginFailed,
            LogEvents.AccountCreated,
            LogEvents.GameAuthFailed,
            LogEvents.SessionLogin,
            LogEvents.SessionLogout,
            LogEvents.SessionReplaced,
            LogEvents.SessionCheatReport,
            LogEvents.CharacterDeleted,
            LogEvents.TradeCompleted,
            LogEvents.BoothSold,
        };

        private static readonly HashSet<string> ExcludedWeb = new HashSet<string>(StringComparer.Ordinal)
        {
            "web.error",
            "web.rpc_connected",
            "web.rpc_lost",
        };

        public static bool IsAudited(LogEvent logEvent)
        {
            if (logEvent == null ||
                !logEvent.Properties.TryGetValue("Event", out var value) ||
                value is not ScalarValue { Value: string eventName })
                return false;

            return IsAudited(eventName);
        }

        public static bool IsAudited(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
                return false;

            if (Exact.Contains(eventName))
                return true;

            if (eventName.StartsWith("gm.", StringComparison.Ordinal))
                return true;

            if (eventName.StartsWith("web.", StringComparison.Ordinal) && !ExcludedWeb.Contains(eventName))
                return true;

            return false;
        }
    }
}
