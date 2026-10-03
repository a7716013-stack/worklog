using System.ComponentModel.DataAnnotations;
namespace WorkJournal.Web.Models;

public class CalendarSyncLink
{
    public int Id { get; set; }
    [MaxLength(450)] public string ApplicationUserId { get; set; } = "";
    public int? WorkLogId { get; set; }
    public WorkLog? WorkLog { get; set; }
    [MaxLength(1024)] public string GoogleEventId { get; set; } = "";
    // The last successfully shared value; null means an outbound creation is pending.
    public string? Baseline { get; set; }
    public bool Retired { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
    [MaxLength(400)] public string? Problem { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
