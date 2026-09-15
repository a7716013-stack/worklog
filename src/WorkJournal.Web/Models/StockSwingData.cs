namespace WorkJournal.Web.Models;
public record SwingStock(string Symbol, string Name, string Market, bool IsEtf);
public record SwingBar(DateOnly Date, decimal? Open, decimal? High, decimal? Low, decimal? Close, decimal? Volume);
public record SwingFlow(DateOnly Date, decimal? Foreign, decimal? Trust);
public record SwingRule(string Group, string Label, int Points, bool? Passed);
public record SwingStrategy(string Name, string Definition, bool? Passed);
public record SwingChartPoint(DateOnly Date, decimal? Open, decimal? High, decimal? Low, decimal? Close, decimal? Volume, decimal? MA5, decimal? MA10, decimal? MA20, decimal? MA60, decimal? RSI, decimal? Macd, decimal? Signal, decimal? Histogram);
public class SwingSnapshot
{
    public DateOnly? Date { get; set; }
    public int? Score { get; set; }
    public int Earned { get; set; }
    public int Available { get; set; }
    public string State { get; set; } = "資料不足";
    public decimal? Close { get; set; }
    public decimal? ChangePercent { get; set; }
    public decimal? MA5 { get; set; }
    public decimal? MA10 { get; set; }
    public decimal? MA20 { get; set; }
    public decimal? MA60 { get; set; }
    public decimal? RSI { get; set; }
    public decimal? Macd { get; set; }
    public decimal? Signal { get; set; }
    public decimal? VolumeRatio { get; set; }
    public decimal? AverageVolume { get; set; }
    public decimal? PriorHigh20 { get; set; }
    public decimal? PriorLow20 { get; set; }
    public decimal? Foreign5 { get; set; }
    public decimal? Trust5 { get; set; }
    public decimal? Foreign20 { get; set; }
    public decimal? Trust20 { get; set; }
    public int? ForeignStreak { get; set; }
    public int? TrustStreak { get; set; }
    public DateOnly? FlowDate { get; set; }
    public List<SwingRule> Rules { get; set; } = [];
    public List<SwingStrategy> Strategies { get; set; } = [];
}
public record SwingAnalysis(SwingStock Stock, SwingSnapshot Summary, List<SwingChartPoint> Chart, string Message);
public record SwingHistory(SwingStock Stock, SwingBar[] Bars, SwingFlow[] Flows, string Message);
public record SwingTrade(DateOnly Entry, DateOnly Exit, decimal EntryPrice, decimal ExitPrice, decimal ReturnPercent, string Reason);
public class SwingBacktest
{
    public string Message { get; set; } = "";
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Observations { get; set; }
    public int ScoredDays { get; set; }
    public int MissingDays { get; set; }
    public int OpenPositions { get; set; }
    public decimal? WinRate { get; set; }
    public decimal? AverageReturn { get; set; }
    public decimal? TotalReturn { get; set; }
    public decimal? MaxDrawdown { get; set; }
    public List<SwingTrade> Trades { get; set; } = [];
}
