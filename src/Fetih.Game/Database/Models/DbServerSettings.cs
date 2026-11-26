namespace Fetih.Game.Database.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("web_server_settings")]
    public class DbServerSettings
    {
        public const byte SingletonId = 1;

        [Key]
        public byte ID { get; set; } = SingletonId;
        public bool MaintenanceEnabled { get; set; }
        public string MaintenanceMessage { get; set; } = string.Empty;
        public string Motd { get; set; } = string.Empty;
        public uint UpdatedBy { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
