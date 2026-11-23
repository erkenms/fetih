namespace Comet.Game
{
    using System;

    // Times are kept in UTC; calendar days (daily limits, arena seasons, dates shown to
    // players) follow the server's configured time zone.
    public static class Clock
    {
        public static TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Local;

        public static DateTime Local(DateTime utc) => Local(utc, Zone);

        public static DateTime Local(DateTime utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTimeFromUtc(utc, zone);

        public static DateTime Today(DateTime utc) => Local(utc).Date;
    }
}
