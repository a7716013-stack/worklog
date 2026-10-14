using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace WorkJournal.Web.Models;

public class ScanProfile
{
    public int Id { get; set; }
    [MaxLength(40)] public string Code { get; set; } = "EquityDefault";
    [MaxLength(20)] public string Version { get; set; } = "1";
    [MaxLength(80)] public string Name { get; set; } = "上市櫃普通股研究";
    [MaxLength(40)] public string UniverseCode { get; set; } = "StrategyEquity";
    [MaxLength(20)] public string UniverseVersion { get; set; } = "1";
    public bool IncludeTWSE { get; set; } = true;
    public bool IncludeTPEx { get; set; } = true;
    public int MinHistoryDays { get; set; } = 120;
    public decimal? MinAvgVolume20D { get; set; }
    public decimal MinAvgTradingValue20D { get; set; } = 20000000;
    public int RadarMinScore { get; set; } = 60;
    public int BenchmarkMinScore { get; set; } = 75;
    public int RecommendationMinScore { get; set; } = 75;
    public int RecommendationGradeScore { get; set; } = 80;
    public int StrongRecommendationMinScore { get; set; } = 90;
    public decimal TrendVolumeRatio { get; set; } = 1.5m;
    public decimal BreakoutVolumeRatio { get; set; } = 2m;
    public int ForeignBuyDays { get; set; } = 3;
    public int TrustBuyDays { get; set; } = 2;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsValid() => MinHistoryDays >= 120 && MinAvgTradingValue20D >= 0 && MinAvgVolume20D is null or >= 0
        && RadarMinScore is >= 0 and <= 100 && BenchmarkMinScore is >= 0 and <= 100
        && RecommendationMinScore >= RadarMinScore && RecommendationGradeScore >= RecommendationMinScore
        && StrongRecommendationMinScore >= RecommendationGradeScore && StrongRecommendationMinScore <= 100
        && TrendVolumeRatio > 0 && BreakoutVolumeRatio > 0 && ForeignBuyDays is >= 1 and <= 5 && TrustBuyDays is >= 1 and <= 5
        && (IncludeTWSE || IncludeTPEx);
}
public class StrategyScanOptions
{
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 15;
    public int RequestsPerHour { get; set; } = 180;
    public int IntervalMinutes { get; set; } = 15;
    public int CutoffHour { get; set; } = 23;
    public ScanProfile Profile { get; set; } = new();
}
public class StrategyScanRun
{
    public long Id { get; set; }
    public DateOnly TradeDate { get; set; }
    public int ProfileId { get; set; }
    [MaxLength(24)] public string Status { get; set; } = "Preparing";
    public int UniverseCount { get; set; }
    public int QualifiedCount { get; set; }
    public int SkippedCount { get; set; }
    public int PendingCount { get; set; }
    public int HitCount { get; set; }
    public int RecommendationCount { get; set; }
    public int ApiCalls { get; set; }
    public int CacheHits { get; set; }
    public long ElapsedMilliseconds { get; set; }
    [MaxLength(1500)] public string Message { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
// Whole-response cache enables resumable history preparation without repeated per-strategy requests.
public class StrategySourceCache
{
    [Key, MaxLength(180)] public string Key { get; set; } = "";
    public string Json { get; set; } = "";
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
public class StrategyEpisode
{
    public long Id { get; set; }
    public int ProfileId { get; set; }
    [MaxLength(12)] public string Symbol { get; set; } = "";
    [MaxLength(40)] public string StrategyCode { get; set; } = "";
    [MaxLength(20)] public string StrategyVersion { get; set; } = "1";
    public DateOnly StartedTradeDate { get; set; }
    public DateOnly LastMatchedDate { get; set; }
    public DateOnly? EndedTradeDate { get; set; }
    [MaxLength(60)] public string EndReason { get; set; } = "";
    [MaxLength(40)] public string EntrySignalLevel { get; set; } = "";
    public bool HasObservationGap { get; set; }
    public bool PriorStateUnknown { get; set; }
    public int ConfirmedDays { get; set; }
    public int GapDays { get; set; }
}
public class StrategyEvaluation
{
    public long Id { get; set; }
    public int ProfileId { get; set; }
    public long RunId { get; set; }
    public DateOnly TradeDate { get; set; }
    [MaxLength(12)] public string Symbol { get; set; } = "";
    [MaxLength(40)] public string StrategyCode { get; set; } = "";
    [MaxLength(20)] public string StrategyVersion { get; set; } = "1";
    [MaxLength(24)] public string EligibilityStatus { get; set; } = "";
    [MaxLength(24)] public string Status { get; set; } = "";
    [MaxLength(500)] public string Reason { get; set; } = "";
    public bool IsFinal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public class StrategySignal
{
    public long Id { get; set; }
    public long EpisodeId { get; set; }
    public StrategyEpisode Episode { get; set; } = null!;
    public int ProfileId { get; set; }
    public DateOnly TradeDate { get; set; }
    [MaxLength(12)] public string Symbol { get; set; } = "";
    [MaxLength(80)] public string StockName { get; set; } = "";
    [MaxLength(8)] public string Market { get; set; } = "";
    [MaxLength(40)] public string StrategyCode { get; set; } = "";
    [MaxLength(20)] public string StrategyVersion { get; set; } = "1";
    [MaxLength(16)] public string SignalType { get; set; } = "NEW";
    [MaxLength(40)] public string SignalLevel { get; set; } = "";
    public int? Score { get; set; }
    [MaxLength(1000)] public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public StrategySignalSnapshot Snapshot { get; set; } = null!;
    public StrategySignalPerformance? Performance { get; set; }
}
public class StrategySignalSnapshot
{
    [Key] public long StrategySignalId { get; set; }
    public decimal Close { get; set; }
    public decimal? MarketHeat { get; set; }
    [MaxLength(80)] public string Industry { get; set; } = "";
    [MaxLength(30)] public string ScoreVersion { get; set; } = "RadarResearch-1.5.1";
    public string Json { get; set; } = "";
    public DateTimeOffset DataAvailableAt { get; set; }
}
public class StrategySignalPerformance
{
    [Key] public long StrategySignalId { get; set; }
    public DateOnly? NextOpenDate { get; set; }
    public decimal? NextOpen { get; set; }
    public bool IsReferenceOnly { get; set; }
    public bool Completed { get; set; }
    public string Json { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; }
}
public class StrategyDailyStock
{
    public long Id { get; set; }
    public int ProfileId { get; set; }
    public DateOnly TradeDate { get; set; }
    [MaxLength(12)] public string Symbol { get; set; } = "";
    [MaxLength(80)] public string StockName { get; set; } = "";
    [MaxLength(8)] public string Market { get; set; } = "";
    public int? RadarScore { get; set; }
    [MaxLength(30)] public string SwingLevel { get; set; } = "";
    public string Json { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
public record StrategySecurity(string Symbol, string Name, string Market, string Industry, string Cfi, bool NormalTrading);
public record StrategyPrice(DateOnly Date, decimal? Open, decimal? High, decimal? Low, decimal? Close, decimal? Volume, decimal? TradingValue)
{
    public bool Valid => Open > 0 && Low > 0 && Close > 0 && High >= Open && High >= Close && Low <= Open && Low <= Close && High >= Low && Volume >= 0;
    public SwingBar ToBar() => new(Date, Open, High, Low, Close, Volume);
}
public record StrategyInput(StrategySecurity Security, DateOnly TradeDate, StrategyPrice[] Prices, SwingFlow[] Flows,
    DateOnly[] Sessions, decimal? RevenueYoY, DateOnly? RevenueMonth, MarketEvent[] Events, bool EventCoverageComplete,
    decimal? Heat, DateTimeOffset AvailableAt);
public record UniverseDecision(string Status, string Reason, decimal? AverageValue20, decimal? AverageVolume20);
public record StrategyDecision(string Code, string Status, string Level, string Reason, string[] Missing);
public class StrategyFacts
{
    public StrategyInput Input { get; set; } = null!;
    public UniverseDecision Eligibility { get; set; } = null!;
    public decimal? MA5 { get; set; }
    public decimal? MA10 { get; set; }
    public decimal? MA20 { get; set; }
    public decimal? MA60 { get; set; }
    public decimal? MA120 { get; set; }
    public decimal? Previous20DHigh { get; set; }
    public decimal? Previous60DHigh { get; set; }
    public decimal? VolumeMA5 { get; set; }
    public decimal? VolumeMA20 { get; set; }
    public decimal? VolumeRatio20 { get; set; }
    public decimal? ChangePercent { get; set; }
    public TechnicalData Technical { get; set; } = new();
    public MarketRadarStock? Radar { get; set; }
    public int? RadarScore { get; set; }
    public Dictionary<string, decimal?> Institutional { get; set; } = [];
    public string SwingLevel { get; set; } = "";
}
public record StrategyHorizon(int Days, DateOnly? Date, string CloseStatus, string OpenStatus,
    decimal? CloseReturn, decimal? OpenReturn, decimal? CloseMfe, decimal? CloseMae, decimal? OpenMfe, decimal? OpenMae);
