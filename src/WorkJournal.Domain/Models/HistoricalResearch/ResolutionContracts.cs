using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch.Components;
namespace WorkJournal.Web.Models.HistoricalResearch.Resolution;

public enum ResolutionStatus { PENDING, SEARCHING, SOURCE_FOUND, CAPTURED, PARSED, UNDER_REVIEW, SUPPORTED, PARTIALLY_SUPPORTED, CONFLICTING, NOT_FOUND, BLOCKED_EXTERNAL, REJECTED, SUPERSEDED }
public enum PublicationClaimType { DatasetPublicationRule, OriginalPublicationTime, CorrectionPublicationRule, RevisionPublicationTime, HistoricalArchiveTimestamp, QueryServiceAvailability }
public enum RevisionSemantics { IMMUTABLE_SOURCE, VERSIONED_SOURCE, LATEST_ONLY_SOURCE, CORRECTABLE_WITH_TIMESTAMP, CORRECTABLE_WITHOUT_TIMESTAMP, UNKNOWN }
public enum ReplaySemantics { Unknown, MarketAvailableReplay, SystemKnownReplay }
public enum ObservationStatus { ELIGIBLE, MISSING, INVALID, FUTURE_KNOWN, UNVERIFIABLE_AVAILABILITY, UNVERIFIABLE_REVISION, UNRESOLVED_IDENTITY, UNRESOLVED_UNIVERSE, UNRESOLVED_CALENDAR, PRICE_BASIS_CONFLICT, CORPORATE_ACTION_UNRESOLVED }
public sealed record AuthorityResolutionTask(Guid ResolutionTaskId, EvidenceCode Component, ClosureDomain AuthorityDomain,
    string RequiredClaimId, string TargetProvider, string TargetDatasetType, DateOnly TargetStart, DateOnly TargetEnd,
    Guid[] CurrentEvidenceIds, string MissingEvidenceDescription, ResolutionStatus AcquisitionStatus, ResolutionStatus ReviewStatus,
    string BlockingReason, int AttemptCount, DateTimeOffset? LastAttemptAt, Guid? ResolvedByEvidenceId, Guid? ResolvedByReviewId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record EvidenceAcquisitionAttempt(Guid AttemptId, Guid ResolutionTaskId, string SourceReference, string Provider,
    DateTimeOffset RequestedAt, DateTimeOffset? ReceivedAt, string AcquisitionStatus, string? ContentType, string? RawDigest,
    string? ArchiveLocation, ResolutionStatus ResultStatus, string? FailureReason, string Operator, Guid? EvidenceId, string Digest);
public sealed record EvidenceSearchResult(Guid ResolutionTaskId, string[] SourcesChecked, string SearchScope, DateTimeOffset SearchDate,
    string Reason, string ClaimState = "UNPROVEN", string Result = "NOT_FOUND");
public sealed record ResolutionAvailability(string FactId, PublicationClaimType ClaimType, VerifiedAvailabilityRule Rule,
    Guid EvidenceId, string Provider, HistoricalDatasetType DatasetType, string PolicyVersion);
public sealed record ResolutionRevision(string FactId, RevisionSemantics Semantics, string Provider, HistoricalDatasetType DatasetType,
    string PolicyVersion, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? SelectedRevisionId, DateTimeOffset? RevisionAvailableAt,
    bool HistoricalVersionRetained, Guid EvidenceId, string ReviewDigest);
public sealed record TrendResolutionRequest(Guid RequestId, ComponentDatasetInput Input, string DependencyVersion,
    ReplaySemantics ReplaySemantics, ResolutionAvailability[] Availability, ResolutionRevision[] Revisions,
    string? SystemKnowledgeReviewDigest, string RequestDigest);
public sealed record HistoricalObservationEligibility(Guid InstrumentId, DateOnly TradeDate, string SourceInstrumentKey,
    string IdentityStatus, string CalendarStatus, string UniverseStatus, string PriceStatus, string AvailabilityStatus,
    string RevisionStatus, string PriceBasisStatus, ObservationStatus FinalStatus, string[] BlockingReasons, string InputDigest);
public sealed record ResolutionClaimRow(ClosureDomain Domain, string RequiredClaim, string CurrentStatus, Guid[] CurrentEvidence,
    string[] MissingEvidence, DateOnly CoverageStart, DateOnly CoverageEnd, string ConflictStatus, string NextResolutionAction);
public sealed record AvailabilityCoverageRow(string Provider, HistoricalDatasetType DatasetType, DateOnly TradeDate,
    string AvailabilityRule, AvailabilityPrecision Precision, string Confidence, Guid[] EvidenceIds, string PolicyVersion, string CoverageStatus);
public sealed record ResolutionCoverage(int? ExpectedObservations, int EligibleObservations, int BlockedObservations,
    decimal? Coverage, HistoricalObservationEligibility[] Observations, string Digest);
public sealed record AuthorityResolutionReport(Guid RequestId, string Status, ResolutionClaimRow[] Claims,
    AuthorityResolutionTask[] Queue, AvailabilityCoverageRow[] AvailabilityCoverageMatrix, ResolutionCoverage Coverage,
    ComponentDatasetEvaluation ComponentEvaluation, string[] BlockingReasons, decimal? AuthorityResolutionProgress, string InputDigest, string Digest);
public sealed record DatasetScopeDecision(Guid OriginalRequestId, Guid NewRequestId, string OriginalRequestDigest, string NewRequestDigest,
    string RemovedPeriod, string RemovedUniverseScope, string Reason, string[] EvidenceGapIds, string ReviewedBy, DateTimeOffset ReviewedAt, string Digest);
public sealed record ResolutionApprovalReceipt(Guid ApprovalId, Guid RequestId, ComponentDataMode DataMode, EvidenceCode Component,
    string CandidateVersion, Guid ParameterSetId, DateOnly CoverageStart, DateOnly CoverageEnd, string UniverseDefinitionVersion,
    Guid[] UniverseSnapshotIds, string CalendarVersion, string DatasetSnapshotId, string[] AvailabilityPolicyVersions,
    string[] RevisionPolicyVersions, string PriceBasisVersion, string[] AuthorityClosureRevisionIds, string[] EvidenceDigests,
    ReplaySemantics ReplaySemantics, string DependencyManifestDigest, string RequestDigest, string EligibilityDigest,
    string ComponentApprovalDigest, string CodeSHA, DateTimeOffset ApprovedAt, string ApprovedBy, string ApprovalDigest);
public sealed record ResolutionApprovalArchive(TrendResolutionRequest Request, AuthorityResolutionReport Report, ResolutionApprovalReceipt Receipt);
public sealed record RealComponentMilestone(string MilestoneId, Guid ApprovalId, EvidenceCode Component, string CommitSHA,
    DateTimeOffset AchievedAt, string ReceiptDigest, string Digest);
public sealed record ComponentDatasetApprovalFailure(Guid RequestId, EvidenceCode Component, string[] FailedGates,
    string[] BlockingClaims, string[] BlockingEvidenceGaps, ResolutionCoverage? CoverageSummary, DateTimeOffset EvaluatedAt,
    string CodeSHA, string InputDigest);
