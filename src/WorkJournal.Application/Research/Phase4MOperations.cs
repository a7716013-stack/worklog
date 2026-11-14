using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record HistoricalEvidenceCard(string TaskId,string SourceVersion,string SubjectScope,string[] Instruments,DateOnly CoverageStart,DateOnly CoverageEnd,
    string[] RawDigests,DateTimeOffset[] RetrievedAt,string PublishedAtEvidence,string AvailableAtEvidence,string RevisionEvidence,string RightsStatus,
    string? Reviewer,string ReviewDecision,string[] ReviewReceipts,string? ResolutionReceipt);
public sealed record ReadinessCheck(string Code,string Status,string Evidence);
public sealed record ProductionReadiness(string Mode,bool CanDispatch,bool ExternalAcquisitionAllowed,ReadinessCheck[] Checks,string[] BlockingReasons);
public sealed record DatasetStage(string Name,string Status,string[] Reasons);
public sealed record TrendFactoryReport(string RunId,DatasetStage[] Stages,SamplePolicy Policy,SampleCell[] Samples,string Baseline,string OosStatus,string FormulaDigest,string Promotion,string Phase5);
public sealed record Phase4MReport(string SchemaVersion,string Version,string Phase,string SourceCodeSha,string ParentDigest,string EvidenceDigest,DateTimeOffset ExportedAt,
    string Semantics,HistoricalEvidenceCard[] Acquisition,ReviewQueueItem[] ReviewQueue,ProductionReadiness Production,TrendFactoryReport Trend,EtlAlert[] Alerts,string Digest);

public static class Phase4MOperations
{
    public static readonly string[] RequiredChecks=["DURABLE_SHARED_STORAGE","MULTI_WORKER_RECOVERY","PROVIDER_PERMISSION","BACKUP_RESTORE","SECRET_HANDLING","OPERATIONAL_ALERTS","ROLLBACK","EXPLICIT_ENABLE_APPROVAL"];
    public static ProductionReadiness Readiness(string mode,ReadinessCheck[] checks)
    {
        if(mode is not ("Disabled" or "Dry-run" or "Approved-enabled")||checks.Select(c=>c.Code).Distinct().Count()!=checks.Length||
            checks.Any(c=>!RequiredChecks.Contains(c.Code)||c.Status is not ("Verified" or "Partial" or "Blocked" or "Not Implemented")||string.IsNullOrWhiteSpace(c.Evidence)))throw new ArgumentException("INVALID_READINESS_CHECKLIST");
        var reasons=RequiredChecks.Where(c=>!checks.Any(x=>x.Code==c&&x.Status=="Verified")).ToArray();
        return new(mode,mode=="Approved-enabled"&&reasons.Length==0,false,checks,reasons);
    }
    public static TrendFactoryReport Trend(Phase4IReport p,Phase4JReport e,ReviewQueueItem[] reviews)
    {
        Phase4JProjection.Validate(e,p);
        if(p.Trend.Policy!=SamplePolicy.Phase4I)throw new InvalidDataException("TREND_SAMPLE_POLICY_CHANGED");
        var invalid=e.Impacts.Length>0||reviews.Any(q=>q.Status=="REVOKED");
        var approved=p.Validation.Provenance.DatasetApprovalId is not null&&!invalid;
        var baseline=approved?p.Trend.Status:"NOT_RUN";
        var claims=Phase4IProjection.Queue(p);var stages=new[]{
            new DatasetStage("Source",e.Sources.Length>0?"CAPTURED_REVIEW_REQUIRED":"BLOCKED",e.Sources.Select(s=>s.LicenseStatus).Distinct().ToArray()),
            new DatasetStage("Authority",!invalid&&claims.All(c=>c.Applicability=="SUPPORTED_FOR_REVIEWED_SCOPE")?"VERIFIED":"BLOCKED",claims.Where(c=>c.Applicability!="SUPPORTED_FOR_REVIEWED_SCOPE").Select(c=>c.Id).Concat(invalid?["SOURCE_REVOKED_OR_INVALIDATED"]:Array.Empty<string>()).ToArray()),
            new DatasetStage("PIT",!invalid&&p.Counts.EligibleObservations>0?"ELIGIBLE_OBSERVATIONS":"BLOCKED",p.Validation.Observations.SelectMany(o=>o.BlockingReasons).Distinct().ToArray()),
            new DatasetStage("Dataset Approval",approved?"APPROVED":"BLOCKED",approved?[]:["INDEPENDENT_SCOPED_APPROVAL_REQUIRED"]),
            new DatasetStage("Sample Sufficiency",approved?(p.Trend.Status=="NOT_RUN"?"NOT_ASSESSED":p.Trend.Status=="COMPLETED"?"SUFFICIENT":"INSUFFICIENT_SAMPLE"):"NOT_ASSESSED",p.Trend.BlockingReasons),
            new DatasetStage("Baseline",baseline,approved?p.Trend.BlockingReasons:["APPROVED_REAL_DATASET_REQUIRED"])};
        return new("REAL_BASELINE_V1",stages,SamplePolicy.Phase4I,p.Trend.Samples,baseline,p.Trend.OosStatus,p.Validation.Provenance.FormulaDigest,"NOT_PROMOTED","LOCKED");
    }
    public static Phase4MReport Create(Phase4IReport p,Phase4JReport e,EtlOperationsReport operations,ReviewQueueItem[] queue,string sha)
    {
        Phase4JProjection.Validate(e,p);
        if(sha.Length!=40||!sha.All(Uri.IsHexDigit)||operations.EvidenceDigest!=e.Digest||queue.Length!=e.Plan.Length||
            !queue.Select(q=>q.TaskId).Order().SequenceEqual(e.Plan.Select(t=>t.TaskId).Order()))throw new InvalidDataException("PHASE4M_BINDING_INVALID");
        var cards=e.Plan.Select(t=>{
            var c=Phase4IProjection.Queue(p).Single(c=>c.Id==t.ClaimKey);var s=e.Sources.Single(s=>s.SourceId==t.SourceId&&s.SourceVersion==t.SourceVersion);
            var proofs=c.Claim.Sources.Where(s=>s.RawDigest==t.SourceVersion).ToArray();
            return new HistoricalEvidenceCard(t.TaskId,t.SourceVersion,t.SubjectDigest,t.Instruments,t.CoverageStart,t.CoverageEnd,
                proofs.Select(s=>s.RawDigest).Distinct().ToArray(),proofs.Select(s=>s.RetrievedAt).ToArray(),"UNPROVEN_UNLESS_ORIGINAL_PUBLICATION_RECEIPT",c.Claim.HistoricalAvailableAtProof,
                c.Claim.RevisionEvidence,s.LicenseStatus,c.Claim.Reviewer,c.Applicability,proofs.SelectMany(s=>s.ReviewDigests).Distinct().ToArray(),c.Claim.ResolutionReceiptDigest);
        }).ToArray();
        var checks=RequiredChecks.Select(c=>new ReadinessCheck(c,"Blocked","DEPLOYMENT_ENVIRONMENT_ACCEPTANCE_REQUIRED; fixture evidence is separate")).ToArray();
        var report=new Phase4MReport("Phase4M-v1","3.2.2","4M",sha,p.Digest,e.Digest,DateTimeOffset.UtcNow,"CONTROLLED_EXPORT_SNAPSHOT_NOT_LIVE_HEALTH",
            cards,queue,Readiness("Disabled",checks),Trend(p,e,queue),operations.Alerts,"");
        return report with{Digest=ResearchMath.Digest(report)};
    }
    public static void Validate(Phase4MReport r,Phase4IReport p,Phase4JReport e)
    {
        Phase4JProjection.Validate(e,p);
        if(r.SchemaVersion!="Phase4M-v1"||r.Version!="3.2.2"||r.Phase!="4M"||r.SourceCodeSha.Length!=40||!r.SourceCodeSha.All(Uri.IsHexDigit)||
            r.Semantics!="CONTROLLED_EXPORT_SNAPSHOT_NOT_LIVE_HEALTH"||r.ExportedAt.Offset!=TimeSpan.Zero||
            r.Acquisition.Length!=e.Plan.Length||r.ReviewQueue.Length!=e.Plan.Length||
            r.ReviewQueue.Select(q=>q.TaskId).Distinct().Count()!=e.Plan.Length||
            r.ReviewQueue.Any(q=>!e.Plan.Any(t=>t.TaskId==q.TaskId&&t.SourceVersion==q.SourceVersion&&t.SubjectDigest==q.SubjectDigest&&t.CoverageStart==q.CoverageStart&&t.CoverageEnd==q.CoverageEnd))||
            r.ParentDigest!=p.Digest||r.EvidenceDigest!=e.Digest||r.Digest!=ResearchMath.Digest(r with{Digest=""})||r.Production.CanDispatch||r.Production.Mode!="Disabled"||
            ResearchMath.Digest(r.Trend)!=ResearchMath.Digest(Trend(p,e,r.ReviewQueue))||
            ResearchMath.Digest(r.Production)!=ResearchMath.Digest(Readiness(r.Production.Mode,r.Production.Checks)))throw new InvalidDataException("PHASE4M_EXPORT_INVALID");
    }
}
