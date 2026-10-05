namespace WorkJournal.Web.Models;

public record MarketQuote(SwingStock Stock, DateOnly Date, decimal Close, decimal? Change, decimal Volume)
{
    public decimal? ChangePercent => Change is decimal change && Close - change > 0 ? change / (Close - change) * 100 : null;
}
public record MarketEvent(string StockId, string Title, string Category, DateTimeOffset PublishedAt,
    string Source, string SourceUrl, string Impact, string Summary);
public record MarketSnapshot(MarketQuote[] Quotes, MarketEvent[] Events, string[] Warnings, DateTimeOffset UpdatedAt);
public record RadarHistory(SwingBar[] Bars, SwingFlow[] Flows, decimal? RevenueYoY, DateOnly? RevenueMonth);
public record RadarScorePart(string Name, int Maximum, int? Points, string Explanation);
public class MarketRadarStock
{
    public required MarketQuote Quote { get; init; }
    public decimal? AverageVolume5 { get; init; }
    public decimal? AverageVolume20 { get; init; }
    public decimal? VolumeRatio5 => AverageVolume5 > 0 ? Quote.Volume / AverageVolume5 : null;
    public decimal? VolumeRatio20 => AverageVolume20 > 0 ? Quote.Volume / AverageVolume20 : null;
    public decimal? VolumeGrowth20 => (VolumeRatio20 - 1) * 100;
    public TechnicalData Technical { get; init; } = new();
    public decimal? ForeignNet { get; init; }
    public string MacdState { get; init; } = "資料不足";
    public RadarScorePart[] Parts { get; init; } = [];
    public int Earned => Parts.Sum(x => x.Points ?? 0);
    public int Available => Parts.Where(x => x.Points.HasValue).Sum(x => x.Maximum);
    public int? Score => Available == 100 ? Earned : null;
    public string Level => Score is null ? "評分不完整" : Score >= 85 ? "強勢追蹤" : Score >= 70 ? "值得追蹤" : Score >= 55 ? "中性觀察" : Score >= 40 ? "偏弱" : "低優先觀察";
    public string[] Reasons { get; init; } = [];
    public string[] RiskFlags { get; init; } = [];
    public MarketEvent[] Events { get; init; } = [];
}
public record MarketRadarResult(DateOnly? TradeDate, DateOnly[] Dates, string Market, MarketQuote[] Quotes,
    MarketRadarStock[] Analysis, MarketEvent[] Events, string[] Warnings, DateTimeOffset UpdatedAt);
