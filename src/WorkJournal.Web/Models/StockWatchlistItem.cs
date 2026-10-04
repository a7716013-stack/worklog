using System.ComponentModel.DataAnnotations;
namespace WorkJournal.Web.Models;
public class StockWatchlistItem
{
    public int Id { get; set; }
    [MaxLength(450)] public string ApplicationUserId { get; set; } = "";
    [MaxLength(6)] public string Symbol { get; set; } = "";
    [MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(10)] public string Market { get; set; } = "";
    public bool IsEtf { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
