namespace Comet.Web.Audit;

using System.ComponentModel.DataAnnotations.Schema;

[Table("web_audit")]
public class DbWebAudit
{
    public ulong ID { get; set; }
    public DateTime Timestamp { get; set; }
    public uint AccountID { get; set; }
    public string Username { get; set; } = "";
    public string? IPAddress { get; set; }
    public string Action { get; set; } = "";
    public string? TargetType { get; set; }
    public string? TargetID { get; set; }
    public string? Summary { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
}
