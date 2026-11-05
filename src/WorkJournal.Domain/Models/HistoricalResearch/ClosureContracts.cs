using WorkJournal.Web.Models.Evidence;
namespace WorkJournal.Web.Models.HistoricalResearch;

// Workflow order, not investment ranking. Separate from the broader Phase 4E evidence domains.
public enum ClosureDomain { TradingCalendar, HistoricalInstrumentIdentity, HistoricalUniverse, HistoricalPriceVolume, Availability, CorporateActionPriceBasis }
public enum ClosureStatus { OPEN, EVIDENCE_CAPTURED, UNDER_REVIEW, PARTIALLY_SUPPORTED, APPROVED, REJECTED, BLOCKED, SUPERSEDED }
public sealed record AuthorityClosureCase(Guid CaseId,ClosureDomain Domain,string Provider,string DatasetType,
    DateOnly TargetStart,DateOnly TargetEnd,string[] RequiredClaims,Guid[] EvidenceIds,Guid[] ReviewIds,
    ClosureStatus CurrentStatus,string[] BlockingReasons,DateTimeOffset OpenedAt,DateTimeOffset? LastReviewedAt,
    DateTimeOffset? ClosedAt,string ClosureDigest,string SubjectDigest);
public sealed record ClosureClaimDecision(string Claim,string Status,Guid[] EvidenceIds,Guid[] ReviewIds,string[] Reasons);
public sealed record ClosureRevision(int Sequence,string PreviousDigest,AuthorityClosureCase Case,string[] ReviewDigests,
    ClosureClaimDecision[] Claims,DateTimeOffset At,string Reviewer,string Reason,string Digest);
public sealed record ClosureReviewRequest(Guid CaseId,string ExpectedRevisionDigest,ClosureStatus NextStatus,
    Guid[] AddEvidenceIds,string[] AddReviewDigests,DateTimeOffset At,string Reviewer,string Reason);
public sealed record ComponentClosureBinding(ClosureDomain Domain,Guid CaseId,string RevisionDigest);
public sealed record ComponentClosureRequest(RealDatasetSubmission Dataset,ComponentClosureBinding[] Closures);
public sealed record ComponentClosureReport(string Status,EvidenceCode[] Components,ClosureDomain[] RequiredDomains,
    ClosureRevision[] Cases,string[] BlockingReasons,string InputDigest,string DatasetInputDigest,string ReportDigest);
