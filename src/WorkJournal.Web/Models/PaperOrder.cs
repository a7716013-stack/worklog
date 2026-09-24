namespace WorkJournal.Web.Models;
public class PaperOrder
{
    public long Id { get; set; }
    public int AccountId { get; set; }
    public Guid ClientRequestId { get; set; }
    public string StockId { get; set; } = "";
    public string StockName { get; set; } = "";
    public bool IsEtf { get; set; }
    public PaperOrderSide Side { get; set; }
    public PaperOrderType OrderType { get; set; }
    public int Quantity { get; set; }
    public decimal? LimitPrice { get; set; }
    public PaperOrderStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FilledAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public decimal? FilledPrice { get; set; }
    public string? RejectReason { get; set; }
    public DateOnly? SubmittedTradeDate { get; set; }
    public DateOnly? EligibleFromTradeDate { get; set; }
    public DateOnly? LastEvaluatedTradeDate { get; set; }
    public DateOnly? FilledTradeDate { get; set; }
}
