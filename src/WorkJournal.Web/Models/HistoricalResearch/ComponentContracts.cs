using WorkJournal.Web.Models.Evidence;
namespace WorkJournal.Web.Models.HistoricalResearch.Components;

public enum ComponentDataMode { Synthetic, RealApproved }
public enum ClaimSupport { SUPPORTED, PARTIALLY_SUPPORTED, UNSUPPORTED, CONFLICTING, UNKNOWN }
public enum SessionKind { NormalTradingDay, ScheduledHoliday, EmergencyClosure, SpecialSession, Unknown }
public enum AuthorityLevel { OfficialTimestamp, OfficialArchivedTimestamp, VerifiedSchedule, OfficialDocumentation, Secondary, Inference }
public enum ComponentAvailability { Available, Missing, Invalid, FutureKnown, Unverifiable, Stale }
public sealed record ComponentAuthorityDependencyManifest(EvidenceCode Component,string ModelVersion,Guid ParameterSetId,
    ClosureDomain[] RequiredAuthorityDomains,string[] RequiredDatasets,string[] RequiredPolicies,string[] RequiredIdentities,
    string[] OptionalAuthorityDomains,string[] ExcludedDependencies,DateTimeOffset CreatedAt,string ManifestVersion,string Digest);
public sealed record AuthorityClaimResult(string ClaimId,ClosureDomain Domain,string ClaimType,string Subject,DateOnly CoverageStart,DateOnly CoverageEnd,
    Guid[] EvidenceIds,string EvidenceDigest,ClaimSupport SupportStatus,string CoverageStatus,string ConflictStatus,string Reviewer,
    DateTimeOffset ReviewedAt,string Reason,string[] KnownGaps,int Revision,string PreviousRevisionDigest,string RevisionDigest);
public sealed record HistoricalTradingSession(string Market,DateOnly TradeDate,SessionKind SessionType,bool? IsTradingDay,TimeOnly? OpenTime,
    TimeOnly? CloseTime,DateTimeOffset EffectiveAt,DateTimeOffset? AvailableAt,Guid SourceEvidenceId,string PolicyVersion);
public sealed record CalendarCoverage(int? ExpectedSessions,int VerifiedSessions,int UnknownSessions,int ScheduledClosures,int EmergencyClosures,decimal? Coverage);
public sealed record HistoricalInstrumentIdentityRecord(Guid InstrumentId,string Market,string Symbol,DateTimeOffset ValidFrom,DateTimeOffset? ValidTo,
    DateOnly? ListingDate,DateOnly? DelistingDate,string? PreviousSymbol,string? NextSymbol,string? MarketTransferFrom,string? MarketTransferTo,
    DateTimeOffset EffectiveAt,DateTimeOffset? AvailableAt,Guid EvidenceId,string RevisionId);
public sealed record HistoricalUniverseSnapshot(Guid UniverseSnapshotId,string Market,DateOnly TradeDate,Guid[] InstrumentIds,
    string EligibilityDefinitionVersion,Guid[] SourceEvidenceIds,Guid[] AuthorityClosureIds,DateTimeOffset CreatedAt,string Digest);
public sealed record HistoricalUniverseMember(Guid UniverseSnapshotId,Guid InstrumentId,string MembershipStatus,string EligibilityReason,
    bool? ListedAtDate,bool? TradableAtDate,Guid SourceEvidenceId);
public sealed record RawRecordLocator(Guid EvidenceId,string ResponseDigest,int RecordIndex,DateOnly TradeDate,string Symbol);
public sealed record HistoricalFactLineage(string FactId,Guid InstrumentId,DateOnly TradeDate,HistoricalDatasetType DatasetType,
    Guid RawEvidenceId,RawRecordLocator RawRecordLocator,string ParserVersion,string ParsedValueDigest,string? RevisionIdentity,
    Guid? AvailableAtPolicyId,string DatasetSnapshotId);
public sealed record HistoricalRevisionPolicy(string Provider,HistoricalDatasetType DatasetType,string PolicyVersion,bool? CanRevise,
    string RevisionIdentifierMethod,string OriginalPublicationRule,string CorrectionPublicationRule,DateOnly EffectiveFrom,DateOnly? EffectiveTo,
    Guid[] EvidenceIds,ClaimSupport ReviewStatus,DateTimeOffset? FrozenAt,string ReviewDigest);
public sealed record HistoricalAvailabilityClaim(string Provider,HistoricalDatasetType DatasetType,DateOnly TradeDate,string PublicationRule,
    string PublicationTimezone,DateTimeOffset? OriginalAvailableAt,DateTimeOffset? RevisionAvailableAt,Guid[] EvidenceIds,
    AuthorityLevel EvidenceType,AvailabilityConfidence Confidence,string PolicyVersion,string FactId,string ReviewDigest);
public sealed record HistoricalCorporateAction(Guid InstrumentId,string ActionType,DateOnly? ExDate,DateOnly? RecordDate,DateOnly? EffectiveDate,
    decimal? AdjustmentFactor,DateTimeOffset? AvailableAt,Guid SourceEvidenceId,string? RevisionId);
public sealed record AuthorityEvidenceRegistry(Guid EvidenceId,string Provider,string DatasetType,string SourceUrl,DateTimeOffset RetrievedAt,
    string RawDigest,string ContentType,DateOnly CoverageStart,DateOnly CoverageEnd,string[] ClaimTypes,AuthorityLevel AuthorityLevel,
    string ArchiveLocation,string ParserVersion,string RegistryVersion);
public sealed record ComponentDatasetApprovalRequest(EvidenceCode Component,string CandidateVersion,Guid ParameterSetId,DateOnly CoverageStart,
    DateOnly CoverageEnd,string UniverseDefinitionVersion,string PriceBasisVersion,Guid[] RequiredAuthorityCaseIds,Guid[] RequiredPolicyIds,
    string RequestedBy,DateTimeOffset RequestedAt,string RequestDigest);
public sealed record ComponentDatasetInput(ComponentDataMode DataMode,ComponentDatasetApprovalRequest Request,ComponentClosureRequest Closure,
    HistoricalTradingSession[] Sessions,HistoricalInstrumentIdentityRecord[] Identities,HistoricalUniverseSnapshot[] Universes,
    HistoricalUniverseMember[] Members,HistoricalFactLineage[] Lineage,HistoricalRevisionPolicy[] RevisionPolicies,
    HistoricalAvailabilityClaim[] AvailabilityClaims,HistoricalCorporateAction[] CorporateActions);
public sealed record ComponentDatasetCoverageReport(int? ExpectedTradingDates,int VerifiedTradingDates,int UnknownTradingDates,
    int? ExpectedUniverseMembers,int VerifiedUniverseMembers,int? UnknownUniverseMembers,int? ExpectedObservations,int VerifiedObservations,
    int FutureKnownExcluded,int UnknownAvailabilityExcluded,int RevisionUnknownExcluded,bool SurvivorshipRisk,bool PriceBasisConsistency,string Digest);
public sealed record ComponentDatasetApprovalReceipt(Guid ApprovalId,EvidenceCode Component,string CandidateVersion,Guid ParameterSetId,
    string DatasetSnapshotId,Guid[] UniverseSnapshotIds,string PriceBasisVersion,string[] AuthorityClosureRevisions,string[] AvailabilityPolicyVersions,
    string[] EvidenceDigests,DateOnly CoverageStart,DateOnly CoverageEnd,DateTimeOffset ApprovedAt,string ApprovedBy,string InputDigest,
    string DatasetDigest,string CoverageReportDigest,string ApprovalDigest);
public sealed record ComponentDatasetEvaluation(string Status,ComponentDataMode DataMode,ComponentAuthorityDependencyManifest Dependency,
    ComponentDatasetCoverageReport Coverage,string DatasetSnapshotId,string DatasetDigest,string[] KnownGaps,string InputDigest,string Digest);
public sealed record ComponentApprovalArchive(ComponentDatasetInput Input,ComponentDatasetEvaluation Evaluation,ComponentDatasetApprovalReceipt Receipt);
public sealed record ApprovalUseRevision(int Sequence,string PreviousDigest,Guid ApprovalId,string Status,DateTimeOffset At,string Reason,string Digest);
public sealed record ComponentBaselineManifest(Guid ResearchRunId,Guid ApprovalId,string ApprovalDigest,EvidenceCode Component,string CandidateVersion,
    Guid ParameterSetId,string DatasetSnapshotId,Guid[] UniverseSnapshotIds,Guid[] BenchmarkVersions,string[] AuthorityClosureRevisions,
    string[] AvailabilityPolicies,string PriceBasisVersion,string[] SourceDigests,string CodeCommitSha,DateTimeOffset CreatedAt,string ManifestDigest);
public sealed record ComponentFailure(string FailureStage,string FailureReason,string InputDigest,string AuthorityState,string CoverageState,DateTimeOffset Timestamp);
public sealed record ParsedPriceRow(RawRecordLocator Locator,HistoricalValue Value,string ParsedValueDigest,string ParserVersion);
