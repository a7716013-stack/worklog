namespace WorkJournal.Web.Models;
public class FundamentalData
{
    public DateOnly? ValuationDate { get; set; }
    public decimal? PE { get; set; }
    public decimal? PB { get; set; }
    public decimal? Yield { get; set; }
    public DateOnly? RevenueMonth { get; set; }
    public decimal? Revenue { get; set; }
    public decimal? RevenueYoY { get; set; }
    public decimal? RevenueMoM { get; set; }
    public string ValuationMessage { get; set; } = "尚無估值資料";
    public string RevenueMessage { get; set; } = "尚無月營收資料";
}
public class TechnicalData
{
    public DateOnly? Date { get; init; }
    public int Samples { get; init; }
    public decimal? MA5 { get; init; }
    public decimal? MA20 { get; init; }
    public decimal? MA60 { get; init; }
    public decimal? RSI14 { get; init; }
    public decimal? MACD { get; init; }
    public decimal? Signal { get; init; }
    public decimal? Histogram { get; init; }
}
public record DailyClose(DateOnly Date, decimal? Close);
