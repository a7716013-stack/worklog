namespace WorkJournal.Web.Models;
public class PaperPosition
{
    public long Id { get; set; }
    public int AccountId { get; set; }
    public string StockId { get; set; } = "";
    public string StockName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal AverageCost { get; set; }
    public decimal CostBasis { get; set; }
    public decimal RealizedProfitLoss { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
