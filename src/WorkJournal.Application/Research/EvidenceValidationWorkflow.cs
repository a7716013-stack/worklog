using WorkJournal.Domain.Research;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.HistoricalResearch.Components;
using WorkJournal.Web.Models.HistoricalResearch.Resolution;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public enum ResearchEligibilityStatus { BLOCKED, READY_FOR_CONTROLLED_BASELINE }
public sealed record EvidenceSourceProof(Guid EvidenceId,string SourceUrl,string RawDigest,DateTimeOffset RetrievedAt,string[] ReviewDigests);
public sealed record ClaimClosureAudit(string ClaimId,string Domain,int Priority,string SubjectDigest,string[] Instruments,
    DateOnly CoverageStart,DateOnly CoverageEnd,EvidenceSourceProof[] Sources,string HistoricalAvailableAtProof,string RevisionEvidence,
    string? Reviewer,DateTimeOffset? ReviewedAt,string CurrentStatus,string[] BlockingReasons,string? ResolutionReceiptDigest);
public sealed record PitGateResult(string Gate,string Status);
public sealed record PitEligibilityEntry(string ObservationId,Guid InstrumentId,string Symbol,DateOnly TradeDate,DateTimeOffset DecisionTime,
    DateTimeOffset? AvailableAt,string[] SourceVersions,string[] AuthorityReceipts,PitGateResult[] Gates,string[] BlockingReasons,
    bool Eligible,string DatasetSnapshotId,string DatasetVersion,Guid VersionSetId,string SourceReportDigest);
public sealed record ComponentReadiness(string Evidence,int Weight,ResearchEligibilityStatus Eligibility,string DatasetStatus,string BaselineStatus,
    string[] BlockingReasons,Guid? ApprovalId,string? ApprovalDigest);
public sealed record ValidationProvenance(string RunId,string ResearchStatus,string ModelVersion,string FormulaDigest,string ParameterChecksum,
    string CodeCommitSha,string DatasetSnapshotId,string DatasetVersion,Guid VersionSetId,string RequestDigest,string SourceReportDigest,
    string EligibilityDigest,string[] AuthorityReceipts,Guid? DatasetApprovalId,string? DatasetApprovalDigest,string? ResearchOutputDigest,
    DateOnly CoverageStart,DateOnly CoverageEnd,string ReplaySemantics);
public sealed record DatasetReadiness(string DatasetSnapshotId,string DatasetVersion,Guid VersionSetId,int RawRealRows,int? ExpectedObservations,
    int EligibleObservations,int BlockedObservations,ResearchEligibilityStatus Status,string[] BlockingReasons);
public sealed record ComponentFeatureDiagnostics(string Evidence,string Status,FeatureValidationResult[] Features,EvidenceValidationResult? Validation,
    string OutcomeStatus,string Scope);
public sealed record EvidenceHypothesisReceipt(CandidateHypothesis Hypothesis,Guid BaselineRunId,string BaselineOutputDigest,string FormulaDigest,string Status,string Digest);
public sealed record EvidenceValidationReport(string SchemaVersion,DateTimeOffset GeneratedAt,string SourceReportDigest,
    ClaimClosureAudit[] Claims,PitEligibilityEntry[] Observations,DatasetReadiness Dataset,ComponentReadiness[] Components,
    ValidationProvenance Provenance,ComponentFeatureDiagnostics[] Diagnostics,string FullModelStatus,string V3Threshold,string Promotion,string Phase5,string Digest);

// No alternative authority registry. Existing gates and controlled receipts remain authoritative.
public sealed class EvidenceValidationWorkflow(string root,string evidenceRoot,string authorityRoot,string closureRoot)
{
    private AuthorityResolutionStore Store=>new(root,evidenceRoot,authorityRoot,closureRoot);
    public EvidenceValidationReport Evaluate(TrendResolutionRequest request,string codeSha,DateTimeOffset at,Guid? approvalId=null)
    {
        if(codeSha.Length!=40||!codeSha.All(Uri.IsHexDigit))throw new ArgumentException("Explicit code SHA required");
        var report=Store.Evaluate(request);
        var data=request.Input.Closure.Dataset.Source.Dataset;
        ResolutionApprovalReceipt? receipt=null;
        if(approvalId.HasValue)
        {
            var archive=Store.Replay(approvalId.Value);
            new ComponentDatasetStore(Path.Combine(root,"components"),evidenceRoot,authorityRoot,closureRoot).Current(approvalId.Value);
            if(archive.Request.RequestDigest!=request.RequestDigest||archive.Report.Digest!=report.Digest)
                throw new InvalidDataException("Approval does not bind current request/evaluation");
            receipt=archive.Receipt;
        }
        var real=request.Input.DataMode==ComponentDataMode.RealApproved&&data.Archives.Length>0&&data.Archives.All(a=>!a.Synthetic);
        var reasons=report.BlockingReasons.Concat(real?[]:new[]{"SYNTHETIC_OR_MISSING_REAL_ARCHIVE"})
            .Concat(receipt is null?new[]{"APPROVED_REAL_DATASET_REQUIRED"}:[]).Distinct().Order(StringComparer.Ordinal).ToArray();
        var status=reasons.Length==0?ResearchEligibilityStatus.READY_FOR_CONTROLLED_BASELINE:ResearchEligibilityStatus.BLOCKED;
        var evidence=new EvidenceAcquisitionStore(evidenceRoot);
        var ledger=new AuthorityResolutionLedger(root,evidenceRoot);
        var reviews=Directory.Exists(Path.Combine(evidenceRoot,"reviews"))?
            Directory.GetFiles(Path.Combine(evidenceRoot,"reviews"),"*.json").Select(p=>evidence.ReadReview(Path.GetFileNameWithoutExtension(p))).ToArray():[];
        var claims=report.Claims.Select(c=>{
            var task=report.Queue.Single(q=>q.AuthorityDomain==c.Domain&&q.RequiredClaimId==c.RequiredClaim);
            var ids=c.CurrentEvidence.Concat(ledger.Attempts(task.ResolutionTaskId).Where(a=>a.EvidenceId.HasValue).Select(a=>a.EvidenceId!.Value)).Distinct().Order().ToArray();
            var sources=ids.Select(id=>{var a=evidence.ReadArtifact(id);return new EvidenceSourceProof(id,a.SourceReference,a.SHA256,a.CapturedAt,
                reviews.Where(v=>v.EvidenceId==id).Select(v=>v.ReviewDigest).Order(StringComparer.Ordinal).ToArray());}).ToArray();
            var binding=request.Input.Closure.Closures.SingleOrDefault(x=>x.Domain==c.Domain);
            var closure=binding is null?null:new AuthorityClosureStore(closureRoot,evidenceRoot).Read(binding.CaseId);
            var reviewed=reviews.Where(v=>ids.Contains(v.EvidenceId)).OrderBy(v=>v.ReviewedAt).LastOrDefault();
            return new ClaimClosureAudit(c.RequiredClaim,c.Domain.ToString(),Priority(c.Domain,c.RequiredClaim),AuthorityClosureStore.Subject(data,c.Domain),
                data.Facts.Select(f=>f.SourceInstrumentKey).Distinct().Order(StringComparer.Ordinal).ToArray(),c.CoverageStart,c.CoverageEnd,sources,
                request.Availability.Length==0?"UNPROVEN":"SEE_OBSERVATION_GATES",request.Revisions.Length==0?"UNPROVEN":"SEE_OBSERVATION_GATES",
                reviewed?.Reviewer??closure?.Reviewer,reviewed?.ReviewedAt??closure?.At,c.CurrentStatus,c.MissingEvidence,closure?.Digest);
        }).OrderBy(c=>c.Priority).ThenBy(c=>c.Domain,StringComparer.Ordinal).ThenBy(c=>c.ClaimId,StringComparer.Ordinal).ToArray();
        var observations=report.Coverage.Observations.Select(o=>{
            var day=data.Dates.Single(d=>d.TradeDate==o.TradeDate);
            var facts=data.Facts.Where(f=>f.SourceInstrumentKey==o.SourceInstrumentKey&&f.TradeDate<=o.TradeDate).ToArray();
            var times=facts.Select(f=>{
                var a=request.Availability.SingleOrDefault(a=>a.FactId==f.FactChecksum);
                var review=a is null?null:evidence.ReadReview(a.Rule.ReviewDigest);
                return AuthorityResolution.Availability(f,a,review,day.DataCutoff).AvailableAt;
            }).ToArray();
            var blocked=o.BlockingReasons.Concat(real?[]:new[]{"SYNTHETIC_OR_MISSING_REAL_ARCHIVE"}).Distinct().ToArray();
            return new PitEligibilityEntry(o.InputDigest,o.InstrumentId,o.SourceInstrumentKey,o.TradeDate,day.DataCutoff,
                o.AvailabilityStatus=="SUPPORTED"&&times.Length>0&&times.All(x=>x.HasValue)?times.Max():null,
                facts.Select(f=>f.RevisionId).Distinct().Order(StringComparer.Ordinal).ToArray(),request.Input.Closure.Closures.Select(c=>c.RevisionDigest).Order(StringComparer.Ordinal).ToArray(),
                [new("HistoricalInstrumentIdentity",o.IdentityStatus),new("TradingCalendar",o.CalendarStatus),new("HistoricalUniverse",o.UniverseStatus),
                 new("PriceVolume",o.PriceStatus),new("Availability",o.AvailabilityStatus),new("Revision",o.RevisionStatus),new("PriceBasis",o.PriceBasisStatus)],
                blocked,real&&o.FinalStatus==ObservationStatus.ELIGIBLE,report.ComponentEvaluation.DatasetSnapshotId,data.DatasetVersion,data.VersionSetId,report.Digest);
        }).ToArray();
        var components=FullEvidenceModel.Weights.Select(c=>new ComponentReadiness(c.Key.ToString(),c.Value,
            c.Key==EvidenceCode.Trend?status:ResearchEligibilityStatus.BLOCKED,c.Key==EvidenceCode.Trend&&receipt is not null?"APPROVED":"BLOCKED","NOT_RUN",
            c.Key==EvidenceCode.Trend?reasons:["INDEPENDENT_COMPONENT_AUTHORITY_PIT_APPROVAL_REQUIRED"],c.Key==EvidenceCode.Trend?receipt?.ApprovalId:null,c.Key==EvidenceCode.Trend?receipt?.ApprovalDigest:null)).ToArray();
        var dataset=new DatasetReadiness(report.ComponentEvaluation.DatasetSnapshotId,data.DatasetVersion,data.VersionSetId,real?data.Facts.Length:0,
            report.Coverage.ExpectedObservations,observations.Count(o=>o.Eligible),observations.Count(o=>!o.Eligible),status,reasons);
        var provenance=new ValidationProvenance("REAL_BASELINE_V1","NOT_RUN",V3CandidateCalculator.Version,FormulaCatalog.Identity.FormulaDigest,data.ParameterChecksum,
            codeSha,dataset.DatasetSnapshotId,data.DatasetVersion,data.VersionSetId,request.RequestDigest,report.Digest,report.Coverage.Digest,
            request.Input.Closure.Closures.Select(c=>c.RevisionDigest).Order(StringComparer.Ordinal).ToArray(),receipt?.ApprovalId,receipt?.ApprovalDigest,null,
            request.Input.Request.CoverageStart,request.Input.Request.CoverageEnd,request.ReplaySemantics.ToString());
        return Seal(new("EvidenceValidation-v1",at,report.Digest,claims,observations,dataset,components,provenance,[],
            "BLOCKED_SIX_INDEPENDENT_COMPONENT_APPROVALS_REQUIRED","UNRESOLVED","NOT_PROMOTED","LOCKED",""));
    }
    public EvidenceValidationReport Diagnose(TrendResolutionRequest request,Guid approvalId,V3EvidenceCandidateV1ParameterSet parameters,string sha,DateTimeOffset at)
    {
        var report=Evaluate(request,sha,at,approvalId);
        if(report.Dataset.Status!=ResearchEligibilityStatus.READY_FOR_CONTROLLED_BASELINE)throw new InvalidOperationException("BLOCKED_BY_AUTHORITY_OR_DATA");
        if(V3CandidateCalculator.ParameterChecksum(parameters)!=report.Provenance.ParameterChecksum)throw new InvalidDataException("Parameter identity mismatch");
        var build=new HistoricalDatasetBuilder().Build(request.Input.Closure.Dataset.Source.Dataset);
        var rows=build.Snapshots.SelectMany(s=>s.Data.UniverseMembers.Select(id=>new ResearchObservation(Guid.Empty,new V3CandidateCalculator().Calculate(s.Data,id,parameters),[]))).ToArray();
        // Explicitly no future outcomes. Publish only the independently approved component.
        var package=FormulaDiagnostics.Build(rows,1,5,"APPROVED_TREND_FEATURES_NO_OUTCOMES");
        return Seal(report with {Diagnostics=[new("Trend","FEATURE_DIAGNOSTICS_ONLY",package.Features.Where(f=>f.FeatureName.StartsWith("Trend.",StringComparison.Ordinal)).ToArray(),
            package.Evidence.Single(e=>e.Evidence=="Trend"),"NOT_RUN_NO_FUTURE_LABELS",package.Scope)]});
    }
    public string RunBaseline(TrendResolutionRequest request,Guid approvalId,ResearchRequest template,string sha,DateTimeOffset at)
    {
        var report=Evaluate(request,sha,at,approvalId);
        if(report.Dataset.Status!=ResearchEligibilityStatus.READY_FOR_CONTROLLED_BASELINE)throw new InvalidOperationException("BLOCKED_BY_AUTHORITY_OR_DATA");
        return Store.Baseline(approvalId,template,sha); // Preserves development/OOS, outcome lineage, purge and replay guards.
    }
    public EvidenceHypothesisReceipt Propose(Guid runId,CandidateHypothesis hypothesis)
    {
        Store.BaselineReplay(runId);
        var baseline=new RealBaselineStore(Path.Combine(root,"components","baselines")).Replay(runId);
        if(baseline.Run.Status!="BASELINE_COMPLETED"||baseline.Evidence.Length==0||hypothesis.Evidence!=baseline.Run.OutputDigest||
            !hypothesis.CreatedBeforeEvaluation||hypothesis.CreatedAt<baseline.Run.CompletedAt||
            !baseline.Diagnostics.Any(d=>d.Component==hypothesis.Component)||
            new[]{hypothesis.CandidateHypothesisId,hypothesis.ObservedProblem,hypothesis.ProposedChange,hypothesis.ExpectedEffect,hypothesis.Risk,hypothesis.DevelopmentScope}.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Replayed real baseline and explicit prospective hypothesis required");
        var receipt=new EvidenceHypothesisReceipt(hypothesis,runId,baseline.Run.OutputDigest,FormulaCatalog.Identity.FormulaDigest,"HYPOTHESIS_ONLY_NOT_CANDIDATE_OR_PROMOTION","");
        return receipt with{Digest=ResearchMath.Digest(receipt)};
    }
    public static EvidenceValidationReport Seal(EvidenceValidationReport r)=>r with {Digest=ResearchMath.Digest(r with {Digest=""})};
    public static void Validate(EvidenceValidationReport r)
    {
        if(r is null||r.Dataset is null||r.Provenance is null||r.Claims is null||r.Components is null||r.Observations is null||r.Diagnostics is null)
            throw new InvalidDataException("Incomplete validation report");
        if(r.SchemaVersion!="EvidenceValidation-v1"||r.Digest!=ResearchMath.Digest(r with {Digest=""})||r.SourceReportDigest!=r.Provenance.SourceReportDigest||
            r.Provenance.FormulaDigest!=FormulaCatalog.Identity.FormulaDigest||r.Provenance.ModelVersion!=V3CandidateCalculator.Version||
            r.Claims.Length!=20||r.Claims.Select(c=>(c.Domain,c.ClaimId)).Distinct().Count()!=20||r.Components.Length!=6||r.Components.Select(c=>c.Evidence).Distinct().Count()!=6||
            r.Components.Any(c=>!FullEvidenceModel.Weights.Any(w=>w.Key.ToString()==c.Evidence&&w.Value==c.Weight)||!Enum.IsDefined(c.Eligibility)||
                c.Eligibility==ResearchEligibilityStatus.READY_FOR_CONTROLLED_BASELINE&&(c.ApprovalId is null||string.IsNullOrWhiteSpace(c.ApprovalDigest)||c.BlockingReasons.Length>0))||
            r.Observations.Select(o=>o.ObservationId).Distinct().Count()!=r.Observations.Length||
            r.Observations.Any(o=>o.SourceReportDigest!=r.SourceReportDigest||o.DatasetSnapshotId!=r.Dataset.DatasetSnapshotId||o.DatasetVersion!=r.Dataset.DatasetVersion||o.VersionSetId!=r.Dataset.VersionSetId||
                o.Eligible&&(o.BlockingReasons.Length>0||o.AvailableAt is null||o.AvailableAt>o.DecisionTime||o.Gates.Length!=7||o.Gates.Any(g=>g.Status is not ("SUPPORTED" or "AVAILABLE")))))
            throw new InvalidDataException("Validation report integrity or identity mismatch");
        if(!Enum.IsDefined(r.Dataset.Status)||r.Dataset.DatasetSnapshotId!=r.Provenance.DatasetSnapshotId||r.Dataset.DatasetVersion!=r.Provenance.DatasetVersion||r.Dataset.VersionSetId!=r.Provenance.VersionSetId||
            r.Dataset.EligibleObservations!=r.Observations.Count(o=>o.Eligible)||r.Dataset.BlockedObservations!=r.Observations.Count(o=>!o.Eligible)||
            r.Dataset.Status==ResearchEligibilityStatus.READY_FOR_CONTROLLED_BASELINE&&(r.Provenance.DatasetApprovalId is null||r.Dataset.BlockingReasons.Length>0)||
            r.Provenance.ResearchStatus!="NOT_RUN"||r.Provenance.ResearchOutputDigest is not null||r.Promotion!="NOT_PROMOTED"||r.Phase5!="LOCKED")
            throw new InvalidDataException("Unsupported release state; controlled research export required");
    }
    public static int Priority(ClosureDomain domain,string claim)=>claim is "HistoricalPublication" or "SourceIdentity"?0:
        claim=="RevisionBehavior"?1:domain switch {ClosureDomain.HistoricalInstrumentIdentity=>2,ClosureDomain.HistoricalUniverse=>3,ClosureDomain.TradingCalendar=>4,_=>5};
}
public sealed class EvidenceValidationReadService(string path)
{
    public EvidenceValidationReport Read() {var r=HistoricalDatasetStore.Read<EvidenceValidationReport>(path);EvidenceValidationWorkflow.Validate(r);return r;}
}
