namespace Comet.Shared.Models
{
    using System.Collections.Generic;

    // What the game server did with a data file it was asked to re-read. With errors nothing
    // was changed; warnings don't stop a reload.
    public class DataReloadResult
    {
        public string File { get; set; }
        public bool Applied { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public string Summary { get; set; }
    }
}
