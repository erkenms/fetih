namespace Comet.Game.Social
{
    using System;

    // Another player this one may trade locked items with, once the trial is over
    public sealed record TradePartner(string Name, DateTime Since)
    {
        public static readonly TimeSpan Trial = TimeSpan.FromDays(3);

        public bool IsFormal(DateTime now) => now >= this.Since + Trial;

        public int TrialMinutes(DateTime now) =>
            this.IsFormal(now) ? 0 : (int)Math.Ceiling((this.Since + Trial - now).TotalMinutes);
    }
}
