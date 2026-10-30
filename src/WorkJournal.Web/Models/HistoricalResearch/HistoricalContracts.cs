using System.Collections.Immutable;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Models.V2;
namespace WorkJournal.Web.Models.HistoricalResearch;

public enum AvailabilityConfidence { Known, DerivedFromVerifiedRule, Unknown }
public enum HistoricalDatasetType { Price, Institutional, Benchmark }
public enum AvailabilityRule { SourceTimestamp, VerifiedLocalPublicationTime, Unknown }
public sealed record FrozenAvailabilityPolicy(Guid PolicyId,string Provider,HistoricalDatasetType DatasetType,string Version,
    DateOnly EffectiveFrom,DateOnly? EffectiveTo,AvailabilityRule Rule,string Timezone,TimeOnly? LocalPublicationTime,
    string EvidenceSource,string EvidenceNote,string ReviewReference,DateTimeOffset CreatedAt,DateTimeOffset? FrozenAt,FoundationStatus Status);
public sealed record SourceArchive(string ArchiveId,string Provider,HistoricalDatasetType DatasetType,string SourceUri,
    DateTimeOffset ObservedAt,DateTimeOffset RetrievedAt,string BodyBase64,string SourceChecksum,string ParserVersion,bool Synthetic);
public sealed record HistoricalValue(decimal? Open=null,decimal? High=null,decimal? Low=null,decimal? Close=null,
    decimal? Volume=null,decimal? TradingValue=null,decimal? Foreign=null,decimal? Trust=null,decimal? Dealer=null);
public sealed record HistoricalFact(string Provider,HistoricalDatasetType DatasetType,string SourceInstrumentKey,DateOnly TradeDate,
    DateTimeOffset EffectiveAt,DateTimeOffset? AvailableAt,DateTimeOffset ObservedAt,DateTimeOffset RetrievedAt,
    string RevisionId,string SourceRecordId,string ArchiveId,HistoricalValue Value,string Unit,string Currency,
    EvidenceStatus QualityStatus,string SourceChecksum,string FactChecksum,Guid? AvailabilityPolicyId,
    AvailabilityConfidence Confidence,string AvailabilityEvidence,string RevisionEvidence,string PriceBasisVersion);
public sealed record HistoricalInstrumentMapping(string Provider,string SourceInstrumentKey,Guid InstrumentId,
    DateOnly ValidFrom,DateOnly? ValidTo,DateTimeOffset? AvailableAt,string SourceEvidence,string Version);
public sealed record HistoricalUniverse(Guid SnapshotId,string DefinitionVersion,DateOnly AsOfDate,DateTimeOffset? AvailableAt,
    ImmutableArray<Guid> Members,string MembershipReason,string SourceEvidence,bool IncludesDelistedHistory,string Checksum);
public sealed record HistoricalBenchmarkMapping(Guid InstrumentId,Guid BenchmarkInstrumentId,Guid BenchmarkVersionId,
    DateOnly ValidFrom,DateOnly? ValidTo,DateTimeOffset? AvailableAt,string Version,string SourceEvidence);
public sealed record HistoricalCalendar(string Version,ImmutableArray<DateOnly> Sessions,DateTimeOffset? AvailableAt,string SourceEvidence);
public sealed record HistoricalPricePolicy(string PriceBasisVersion,string Basis,string CorporateActionPolicyVersion,
    string SourceEvidence,string ReviewReference,bool Frozen);
public sealed record HistoricalResearchDate(DateOnly TradeDate,DateTimeOffset DataCutoff);
public sealed record HistoricalBuildRequest(string DatasetVersion,string BuildVersion,DateTimeOffset CreatedAt,
    Guid VersionSetId,Guid ParameterSetId,string ParameterChecksum,HistoricalCalendar Calendar,HistoricalPricePolicy PricePolicy,
    ImmutableArray<HistoricalResearchDate> Dates,ImmutableArray<SourceArchive> Archives,ImmutableArray<HistoricalFact> Facts,
    ImmutableArray<FrozenAvailabilityPolicy> Policies,ImmutableArray<HistoricalInstrumentMapping> Instruments,
    ImmutableArray<HistoricalUniverse> Universes,ImmutableArray<HistoricalBenchmarkMapping> Benchmarks);
public sealed record HistoricalFactDecision(string FactChecksum,DateOnly ResearchDate,DateTimeOffset DataCutoff,
    Guid? InstrumentId,DateTimeOffset? ResolvedAvailableAt,bool Eligible,string Reason);
public sealed record DatasetCoverage(string Domain,int Expected,int Available,decimal? Coverage,decimal? MissingRate);
public sealed record DatasetQualityReport(int RawRecords,int DuplicateRecords,int InvalidRecords,int StaleRecords,
    decimal? DuplicateRate,decimal? InvalidRate,decimal? StaleRate,ImmutableArray<DatasetCoverage> Coverage,
    int RequestedDates,int BuiltDates,int InstrumentCount);
public sealed record HistoricalBuildResult(string DatasetSnapshotId,string DatasetChecksum,string DatasetVersion,string BuildVersion,
    DateTimeOffset CreatedAt,DateTimeOffset FinalizedAt,string Status,ImmutableArray<ResearchSnapshot> Snapshots,
    ImmutableArray<HistoricalFactDecision> Decisions,DatasetQualityReport Quality,ImmutableArray<ResearchGap> Gaps,
    string InputDigest)
{
    public string PromotionStatus => "BLOCKED_BY_DATA_OR_RESEARCH_PENDING";
}
public sealed record ResearchCandidateRegistration(string CandidateVersion,string EvidenceCode,ImmutableArray<string> FeatureVersions,
    ImmutableArray<string> NormalizationVersions,Guid ParameterSetId,string ParameterChecksum,DateTimeOffset CreatedAt,
    string? SupersedesCandidateVersion,string Status,string Reason);
public sealed record DatasetCapture(SourceArchive Archive,ImmutableArray<HistoricalFact> Facts,ImmutableArray<ResearchGap> Gaps);
