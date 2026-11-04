using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Domain.Research;
public enum FormulaLifecycle { DRAFT,CANDIDATE,IMPLEMENTED,FROZEN_CANDIDATE,RESEARCHED,SELECTED,PROMOTION_CANDIDATE,PRODUCTION_FROZEN,RETIRED }
public enum DatasetLifecycle { CAPTURED,PARSED,PIT_EVALUATED,APPROVAL_PENDING,APPROVED_COMPONENT,APPROVED_FULL,BLOCKED }
public enum ResearchLifecycle { NOT_RUN,BLOCKED_BY_DATA,BLOCKED_BY_AUTHORITY,RUNNING,COMPLETED,INSUFFICIENT,FAILED }
public enum PromotionLifecycle { NOT_ELIGIBLE,UNDER_REVIEW,PROMOTION_CANDIDATE,PROMOTED,REJECTED }
public sealed record ResearchIdentity(string ModelVersion,string FormulaDigest,string ParameterSetId,string ParameterChecksum,string DatasetApprovalId,
    string DatasetSnapshotId,string UniverseSnapshotId,string BenchmarkVersion,string CalendarVersion,string AvailabilityPolicyVersion,string ProtocolVersion,string CodeCommitSha);
public sealed record CandidateHypothesis(string CandidateHypothesisId,string Component,string ObservedProblem,string Evidence,string ProposedChange,string ExpectedEffect,string Risk,string DevelopmentScope,DateTimeOffset CreatedAt,bool CreatedBeforeEvaluation);
public sealed record ComparisonEvidence(decimal? Coverage,decimal? Ic,decimal? RankIc,decimal? QuantileMonotonicity,decimal? Stability,decimal? Saturation,decimal? Correlation,int Complexity);
public sealed record CandidateComparison(string ComparisonId,string HypothesisId,string Baseline,string Candidate,ResearchIdentity Identity,
    ComparisonEvidence BaselineEvidence,ComparisonEvidence CandidateEvidence,string DevelopmentScope,DateTimeOffset EvaluatedAt,string Decision,string ReviewRationale);
public sealed record CandidateFreezeReceipt(string CandidateVersion,string ParameterSet,string ParameterChecksum,string FormulaDigest,string CodeCommitSha,
    string DatasetDevelopmentScope,string ProtocolVersion,DateTimeOffset FreezeTimestamp,string ComparisonId,string ReceiptDigest);
public sealed record CalibrationReceipt(string Id,string ParameterSet,string ParameterChecksum,ResearchPeriod TrainingWindow,DateTimeOffset AsOf,
    string Method,string UniverseSnapshotId,string Digest);
public static class CandidateGovernance
{
    public static void Validate(CandidateHypothesis h,CandidateComparison c)
    {
        if(new[]{h.CandidateHypothesisId,h.Component,h.ObservedProblem,h.Evidence,h.ProposedChange,h.ExpectedEffect,h.Risk,h.DevelopmentScope,c.ReviewRationale}.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("Hypothesis/evidence/review required");
        if(!h.CreatedBeforeEvaluation||h.CreatedAt>=c.EvaluatedAt||h.CandidateHypothesisId!=c.HypothesisId||h.DevelopmentScope!=c.DevelopmentScope)throw new ArgumentException("Hypothesis must precede matching development evaluation");
        if(c.Baseline==c.Candidate||c.Candidate=="V3EvidenceCandidate-v1")throw new ArgumentException("Candidate-v1 is immutable");
        if(c.Decision is not ("KEEP_V1" or "REJECTED" or "INSUFFICIENT" or "SELECT_CANDIDATE"))throw new ArgumentException("Explicit multi-evidence review required");
        if(c.Decision=="SELECT_CANDIDATE"&&new[]{c.CandidateEvidence.Coverage,c.CandidateEvidence.Ic,c.CandidateEvidence.RankIc,c.CandidateEvidence.QuantileMonotonicity,c.CandidateEvidence.Stability,c.CandidateEvidence.Saturation,c.CandidateEvidence.Correlation}.Any(x=>x is null))throw new ArgumentException("Incomplete comparison cannot select a winner");
    }
    public static CandidateFreezeReceipt Freeze(CandidateHypothesis h,CandidateComparison c,DateTimeOffset at,DateOnly oosStart)
    {
        Validate(h,c);
        if(c.Decision is not ("KEEP_V1" or "SELECT_CANDIDATE")||at<c.EvaluatedAt||DateOnly.FromDateTime(at.UtcDateTime)>=oosStart)throw new ArgumentException("Freeze must follow development selection and precede OOS");
        var id=c.Identity;
        if(new[]{id.ParameterSetId,id.ParameterChecksum,id.FormulaDigest,id.CodeCommitSha,id.ProtocolVersion,id.DatasetApprovalId}.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("Complete approved research identity required");
        var r=new CandidateFreezeReceipt(c.Decision=="KEEP_V1"?c.Baseline:c.Candidate,id.ParameterSetId,id.ParameterChecksum,id.FormulaDigest,id.CodeCommitSha,c.DevelopmentScope,id.ProtocolVersion,at,c.ComparisonId,"");
        return r with {ReceiptDigest=ResearchMath.Digest(r)};
    }
    public static CalibrationReceipt Calibrate(string id,string parameterSet,string checksum,ResearchPeriod train,DateTimeOffset asOf,DateOnly firstValidationDate,string method,string universe)
    {
        if(train.End<train.Start||train.End>=firstValidationDate||train.End>DateOnly.FromDateTime(asOf.UtcDateTime)||new[]{id,parameterSet,checksum,universe}.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("Train-only historical calibration required");
        if(method is not ("Linear" or "InverseLinear" or "HistoricalPercentile" or "WinsorizedPercentile" or "Piecewise" or "Peak/Hump" or "ZScoreToCDF"))throw new ArgumentException("Unknown calibration family");
        var r=new CalibrationReceipt(id,parameterSet,checksum,train,asOf,method,universe,"");return r with {Digest=ResearchMath.Digest(r)};
    }
}
