namespace WorkJournal.Web.Models;
public class PaperTrade
{
    public long Id { get; set; }
    public int AccountId { get; set; }
    public long OrderId { get; set; }
    public int ExecutionSequence { get; set; } = 1;
    public string StockId { get; set; } = "";
    public string StockName { get; set; } = "";
    public PaperOrderSide Side { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal Commission { get; set; }
    public decimal TransactionTax { get; set; }
    public decimal NetCashAmount { get; set; }
    public decimal RealizedProfitLoss { get; set; }
    public DateOnly QuoteTradeDate { get; set; }
    public DateTimeOffset ExecutedAt { get; set; } = DateTimeOffset.UtcNow;
}
