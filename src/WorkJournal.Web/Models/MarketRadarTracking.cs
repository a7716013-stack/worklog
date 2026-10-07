using System.ComponentModel.DataAnnotations;
namespace WorkJournal.Web.Models;

public class MarketRadarRecommendation
{
    public long Id { get; set; }
    public DateOnly TradeDate { get; set; }
    [MaxLength(6)] public string StockId { get; set; } = "";
    [MaxLength(120)] public string StockName { get; set; } = "";
    [MaxLength(10)] public string Market { get; set; } = "";
    public bool IsEtf { get; set; }
    [MaxLength(80)] public string Industry { get; set; } = "未知";
    [MaxLength(30)] public string Category { get; set; } = "Incomplete";
    [MaxLength(30)] public string ScoreVersion { get; set; } = "V1-1.2.3";
    public decimal ClosePrice { get; set; }
    public int? Score { get; set; }
    public int EarnedScore { get; set; }
    public int AvailableScore { get; set; }
    public int? MomentumPoints { get; set; }
    public int? VolumePoints { get; set; }
    public int? TechnicalPoints { get; set; }
    public int? ChipPoints { get; set; }
    public int? FundamentalPoints { get; set; }
    public int? EventPoints { get; set; }
    public int? ForeignBuyStreak { get; set; }
    public int? ForeignSellStreak { get; set; }
    public int? TrustBuyStreak { get; set; }
    public int? TrustSellStreak { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public string ResearchJson { get; set; } = "{}";
    public DateTimeOffset SourceUpdatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public MarketRadarPerformance? Performance { get; set; }
}
public class MarketRadarCaptureBatch
{
    public DateOnly TradeDate { get; set; }
    public int Count { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public class MarketRadarDailySelection
{
    public long Id { get; set; }
    public DateOnly TradeDate { get; set; }
    public int Rank { get; set; }
    public long RecommendationId { get; set; }
    public MarketRadarRecommendation Recommendation { get; set; } = null!;
}
public class MarketRadarPerformance
{
    public long Id { get; set; }
    public long RecommendationId { get; set; }
    public MarketRadarRecommendation Recommendation { get; set; } = null!;
    public decimal? Return1D { get; set; }
    public decimal? Return3D { get; set; }
    public decimal? Return5D { get; set; }
    public decimal? Return10D { get; set; }
    public decimal? Return20D { get; set; }
    public decimal? Mfe5D { get; set; }
    public decimal? Mae5D { get; set; }
    public decimal? Mfe20D { get; set; }
    public decimal? Mae20D { get; set; }
    public DateOnly? EntryDate { get; set; }
    public decimal? EntryPrice { get; set; }
    public decimal? OpenReturn1D { get; set; }
    public decimal? OpenReturn3D { get; set; }
    public decimal? OpenReturn5D { get; set; }
    public decimal? OpenReturn10D { get; set; }
    public decimal? OpenReturn20D { get; set; }
    public string DetailJson { get; set; } = "{}";
    [MaxLength(120)] public string Status { get; set; } = "等待後續行情";
    public DateOnly? EvaluatedThroughTradeDate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public class MarketRadarPersonalTracking
{
    public long Id { get; set; }
    [MaxLength(450)] public string ApplicationUserId { get; set; } = "";
    public long RecommendationId { get; set; }
    public MarketRadarRecommendation Recommendation { get; set; } = null!;
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public string PerformanceJson { get; set; } = "{}";
    public bool Completed { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
public class MarketRadarPaperTradeLink
{
    public long Id { get; set; }
    [MaxLength(450)] public string ApplicationUserId { get; set; } = "";
    public long RecommendationId { get; set; }
    public MarketRadarRecommendation Recommendation { get; set; } = null!;
    public long? PaperOrderId { get; set; }
    public long OriginalPaperOrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public record RadarResearch(string Industry, int? ForeignBuy, int? ForeignSell, int? TrustBuy, int? TrustSell, MarketEvent[] Events,decimal? TrustNetToday=null,string[]? Warnings=null);
public record RadarCaptureResult(int Captured,int Skipped,string Message);
public record RadarHorizon(int Days,DateOnly? Date,decimal? Price,decimal? Return,decimal? Mfe,decimal? Mae);
public record RadarEvaluation(DateOnly? EntryDate,decimal? EntryPrice,RadarHorizon[] Horizons,string Status);
