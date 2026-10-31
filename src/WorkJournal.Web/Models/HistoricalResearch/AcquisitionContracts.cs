using System.Collections.Immutable;
using WorkJournal.Web.Models.Evidence;
namespace WorkJournal.Web.Models.HistoricalResearch;

public enum EvidenceDomain { Availability, Universe, Benchmark, CorporateAction, Calendar }
public enum AcquisitionStatus { Pending, Searching, Captured, UnderReview, Verified, Rejected, Blocked, Superseded }
public enum AvailabilityPrecision { ExactTimestamp, TimeWindow, EndOfSession, EndOfCalendarDay, NextTradingSession, Unknown }
public sealed record EvidenceAcquisitionTask(Guid TaskId,string Provider,string DatasetType,EvidenceDomain EvidenceDomain,
    DateOnly TargetPeriodStart,DateOnly TargetPeriodEnd,AvailabilityEvidenceType RequiredEvidenceType,
    AcquisitionStatus Status,int AttemptCount,DateTimeOffset? LastAttemptAt,string LastResult,string SourceReference,
    Guid? CapturedEvidenceId,string BlockingReason);
public sealed record AcquisitionTaskRevision(int Sequence,string PreviousDigest,EvidenceAcquisitionTask Task,
    DateTimeOffset At,string Reviewer,string Reason,string? ReviewDigest,string Digest);
public sealed record AcquisitionPlan(Guid AcquisitionRunId,string Provider,DateOnly PeriodStart,DateOnly PeriodEnd,
    EvidenceAcquisitionTask[] Tasks);
public sealed record EvidenceArtifact(Guid EvidenceId,Guid TaskId,string Provider,string DatasetType,EvidenceDomain Domain,
    string SourceReference,DateTimeOffset CapturedAt,string ContentType,long ByteLength,string SHA256,string ParserVersion,
    DateOnly TargetStart,DateOnly TargetEnd,string MetadataDigest);
public sealed record AcquisitionFailure(Guid TaskId,string SourceReference,string Reason);
public sealed record EvidenceAcquisitionManifest(Guid AcquisitionRunId,DateTimeOffset StartedAt,DateTimeOffset CompletedAt,
    string Provider,string[] DatasetTypes,DateOnly RequestedStart,DateOnly RequestedEnd,EvidenceArtifact[] CapturedArtifacts,
    AcquisitionFailure[] FailedArtifacts,string InputDigest,string OutputDigest,string Status,EvidenceAcquisitionTask[] Tasks);
public sealed record EvidenceReviewReceipt(Guid ReviewId,Guid EvidenceId,string Reviewer,DateTimeOffset ReviewedAt,string Claim,
    string[] Supported,string[] Unsupported,string[] Ambiguous,DateOnly EffectiveFrom,DateOnly EffectiveTo,string Timezone,
    string Notes,string EvidenceDigest,string ReviewDigest);
// Bounds are source-supported instants for a particular observation, never inferred from the capture time.
public sealed record VerifiedAvailabilityRule(string Version,AvailabilityPrecision Precision,DateTimeOffset? LowerBound,
    DateTimeOffset? UpperBound,string AvailabilityResolutionPolicyVersion,string ReviewDigest,DateOnly EffectiveFrom,DateOnly EffectiveTo);
public sealed record AvailabilityResolution(DateTimeOffset? AvailableAt,string Reason,string PolicyVersion);
public sealed record HistoricalAvailabilityMatrixRow(string Provider,string DatasetType,DateOnly Start,DateOnly End,
    Guid[] Authorities,Guid[] Evidence,string AvailabilityRule,int? Expected,int Resolved,string Status);
public sealed record EvidenceDomainStatus(EvidenceDomain Domain,string EvidenceStatus,string AuthorityStatus,
    string CoverageStatus,string[] KnownGaps);
public sealed record DomainAuthorityBinding(EvidenceDomain Domain,string SubjectDigest,Guid EvidenceId,string ReviewDigest,
    Guid AuthorityId,string AuthorityRevisionDigest,string[] RequiredClaims,DateOnly CoverageStart,DateOnly CoverageEnd);
public sealed record EvidenceReadinessReport(EvidenceCode Evidence,string FormulaStatus,string[] RequiredAuthorities,
    Guid[] ApprovedAuthorities,string[] MissingAuthorities,decimal? DataCoverage,bool ResearchReady,string[] ReasonCodes);
public sealed record ComponentDatasetApprovalReceipt(EvidenceCode EvidenceCode,string DatasetSnapshotId,Guid[] AuthorityIds,
    Guid[] UniverseSnapshotIds,Guid[] BenchmarkVersionIds,string CalendarVersion,string PriceBasisVersion,
    DateOnly CoverageStart,DateOnly CoverageEnd,string InputDigest,string ApprovalDigest,DateTimeOffset ApprovedAt);
public sealed record FullModelDatasetApprovalReceipt(Guid DatasetApprovalId,string DatasetSnapshotId,
    ComponentDatasetApprovalReceipt[] Components,int ApprovedWeight,DateOnly CoverageStart,DateOnly CoverageEnd,
    string InputDigest,string ApprovalDigest,DateTimeOffset ApprovedAt);
public sealed record RealDatasetSubmission(AuthoritySubmission Source,DomainAuthorityBinding[] DomainBindings,
    EvidenceCode[] RequestedComponents,DatasetApprovalReview? Review);
public sealed record FirstApprovedRealPitDatasetReport(string Status,Guid? DatasetApprovalId,DateOnly CoverageStart,DateOnly CoverageEnd,
    int TradingDates,int InstrumentCount,int ObservationCount,EvidenceDomainStatus[] Domains,EvidenceReadinessReport[] EvidenceCoverage,
    ComponentDatasetApprovalReceipt[] ComponentApprovals,FullModelDatasetApprovalReceipt? FullApproval,
    string[] KnownGaps,string InputDigest,string ApprovalDigest);
public sealed record UniverseIdentityFact(Guid InstrumentId,string Symbol,string EventType,DateOnly ValidFrom,DateOnly? ValidTo,
    DateTimeOffset? AvailableAt,string SourceFactDigest,Guid AuthorityId);
public sealed record UniverseCoverageReport(DateOnly TradeDate,int? ExpectedCount,int ResolvedCount,int? UnresolvedCount,
    int ListedCount,int DelistedHistoricalCount,int UnknownIdentityCount,string CoverageStatus,string[] KnownGaps,Guid[] Members,string MemberDigest);
public sealed record BenchmarkObservation(Guid BenchmarkId,DateOnly TradeDate,decimal Value,string PriceBasis,
    DateTimeOffset? AvailableAt,Guid AuthorityId,string SourceFactId,string Digest);
public sealed record CorporateActionFact(Guid InstrumentId,string ActionType,DateTimeOffset? AnnouncementAt,
    DateTimeOffset? AvailableAt,DateOnly? ExDate,DateOnly? RecordDate,DateOnly? PaymentDate,DateTimeOffset? EffectiveAt,
    string SourceFactDigest,Guid AuthorityId);
public sealed record PriceBasisCompatibility(string Version,string StockBasisVersion,string BenchmarkBasisVersion,bool Compatible,string ReviewDigest);
