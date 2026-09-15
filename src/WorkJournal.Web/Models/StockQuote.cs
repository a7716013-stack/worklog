namespace WorkJournal.Web.Models;

public class StockQuote
{
    public FundamentalData Fundamentals { get; init; } = new();
    public TechnicalData Technicals { get; init; } = new();
    public DateOnly? TradeDate { get; init; }
    public string? Industry { get; init; }
    public string? Market { get; init; }
    public string Symbol { get; init; } = "";
    public string? Name { get; init; }
    public decimal? CurrentPrice { get; init; }
    public decimal? ChangePercent { get; init; }
    public decimal? OpenPrice { get; init; }
    public decimal? HighPrice { get; init; }
    public decimal? LowPrice { get; init; }
    public long? Volume { get; init; }
}
