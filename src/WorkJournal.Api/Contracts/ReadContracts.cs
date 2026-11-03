using System.ComponentModel.DataAnnotations;
namespace WorkJournal.Api.Contracts;
public sealed class PageQuery
{
    [Range(1,1000000)] public int Page {get;set;}=1;
    [Range(1,200)] public int PageSize {get;set;}=50;
}
public sealed record PageDto<T>(T[] Items,int Page,int PageSize,int TotalCount)
{
    public static PageDto<T> From(IEnumerable<T> rows,PageQuery q) {var all=rows.ToArray();return new(all.Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToArray(),q.Page,q.PageSize,all.Length);}
}
public sealed record HealthDto(string Status,string Version,string? CommitSha);
public sealed record ReadinessDto(string Status,bool ApplicationStarted,bool ConfigurationReady,bool DatabaseReachable);
public sealed record StockDto(string Symbol,string Name,string Market,bool IsEtf);
public sealed record QuoteDto(StockDto Stock,DateOnly Date,decimal Close,decimal? Change,decimal? ChangePercent,decimal Volume);
public sealed record ScorePartDto(string Name,int Maximum,int? Points,string Explanation);
public sealed record TechnicalDto(DateOnly? Date,int Samples,decimal? Ma5,decimal? Ma20,decimal? Ma60,decimal? Rsi14,decimal? Macd,decimal? Signal,decimal? Histogram);
public sealed record EventDto(string Instrument,string Title,string Category,DateTimeOffset PublishedAt,string SourceUrl,string Impact,string Summary);
public sealed record RadarAnalysisDto(QuoteDto Quote,int? Score,int Earned,int Available,string Level,ScorePartDto[] Parts,string[] Reasons,string[] RiskFlags,decimal? AverageVolume5,decimal? AverageVolume20,decimal? VolumeRatio5,decimal? VolumeRatio20,decimal? VolumeGrowth20,decimal? ForeignNet,string MacdState,TechnicalDto Technical,EventDto[] Events);
public sealed record RadarDto(string Market,DateOnly? TradeDate,DateOnly[] Dates,DateTimeOffset GeneratedAt,PageDto<QuoteDto> Quotes,PageDto<RadarAnalysisDto> Analysis,PageDto<EventDto> Events,string[] Warnings);
public sealed record IndustryDto(string Instrument,string Industry);
public sealed record ComponentDto(string Component,decimal Weight);
public sealed record ModelDto(string ModelVersion,string Status,ComponentDto[] Components,decimal? V3Threshold,string ThresholdStatus,string ScoreMeaning);
public sealed record ClaimDto(string ClaimId,string Domain,string Status,DateOnly CoverageStart,DateOnly CoverageEnd,Guid[] EvidenceIds,string[] BlockingReasons,string Revision);
public sealed record DomainDto(string Domain,string Status,int RequiredClaims,int SupportedClaims);
public sealed record CoverageDto(string Status,int RealRawRows,int EligibleObservations,int ApprovedAuthorityDomains,int ApprovedDatasets,string FirstRealPitEligibleObservation,string FirstApprovedRealComponentDataset,string FirstRealComponentBaseline,string Revision);
public sealed record MetricsDto(string Status,decimal? Ic,decimal? RankIc,decimal? Quantile,decimal? Ablation);
public sealed record RunDto(string RunId,string Status,string DataMode,string ModelVersion,string ParameterSetId,string DatasetSnapshotId,string? DatasetApprovalId,string CodeCommitSha,DateTimeOffset CreatedAt,string[] MetricRefs);
public sealed record ApprovalDto(string ApprovalId,string Component,string DatasetSnapshotId,string Status);
public sealed record AccountDto(string Name,decimal InitialCash,decimal Cash,decimal RealizedProfitLoss,DateTimeOffset UpdatedAt);
public sealed record PositionDto(string Instrument,int Quantity,decimal CostBasis,decimal? CurrentPrice,decimal? MarketValue,decimal? UnrealizedProfitLoss);
public sealed record OrderDto(string OrderId,Guid ClientRequestId,string Instrument,string Side,string OrderType,int Quantity,decimal? LimitPrice,string Status,DateTimeOffset CreatedAt,decimal? FilledPrice);
public sealed record TradeDto(string TradeId,string OrderId,string Instrument,string Side,int Quantity,decimal Price,decimal Commission,decimal TransactionTax,decimal NetCashAmount,DateOnly TradeDate,DateTimeOffset ExecutedAt);
