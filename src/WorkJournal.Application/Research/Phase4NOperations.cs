using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record DeploymentEvidence(string Code,string Environment,string SourceCommit,string EvidenceType,string? EvidenceDigest,
    DateTimeOffset? VerifiedAt,string? Reviewer,string Result,DateTimeOffset? ExpiresAt,string RevalidationRule,string RequiredNextAction);
public sealed record AcceptanceCase(string Code,string Result,string EvidenceClass,string RequiredNextAction);
public sealed record ResolutionPacket(string TaskId,string ClaimId,string Subject,string[] Instruments,DateOnly CoverageStart,DateOnly CoverageEnd,
    string[] SourceVersions,string[] RawDigests,string OriginalPublicationEvidence,string AvailableAtEvidence,string RevisionEvidence,string RightsStatus,
    string[] ExistingReviewReceipts,string[] BlockingReasons,string RequiredNextAction,string? AssignedReviewer,string[] RevocationImpact,int Priority);
public sealed record BaselineReadinessDecision(string Status,string ExecutionStatus,string[] BlockingReasons,string AuthorityStatus,string DatasetStatus,
    string SampleStatus,string FormulaDigest,SamplePolicy Policy,string OosStatus,string Promotion,string Phase5);
public sealed record Phase4NReport(string SchemaVersion,string Version,string Phase,string SourceCodeSha,string ParentDigest,string EvidenceDigest,string WorkbenchDigest,
    DateTimeOffset ExportedAt,string EvidenceClass,string Environment,DeploymentEvidence[] DeploymentEvidence,AcceptanceCase[] ProductionAcceptance,
    ProductionReadiness Production,ResolutionPacket[] Packets,BaselineReadinessDecision Baseline,ResearchCounts Counts,DatasetBuildReport[] Datasets,string Digest);

// Read-only projections. Existing Authority/PIT/Dataset approvals remain authoritative.
public static class Phase4NOperations
{
    public const string EnvironmentName="worklog-a7716013-test/Production";
    public static readonly string[] AcceptanceCodes=["GOOGLE_OAUTH","MEMBER_AB_PAPER_PORTFOLIO","PAPER_ORDER_IDOR","WATCHLIST_STRATEGY_RECOMMENDATION",
        "ANONYMOUS_API_MVC","LOGOUT_COOKIE_REVOCATION","SESSION_EXPIRATION","CSRF","RETURN_URL","PUBLIC_MARKET_QUOTES"];
    public static DeploymentEvidence[] PendingEvidence(string sha)=>Phase4MOperations.RequiredChecks.Select(c=>new DeploymentEvidence(c,EnvironmentName,sha,
        "MISSING_DEPLOYMENT_ACCEPTANCE",null,null,null,"BLOCKED",null,"Revalidate on deployment, configuration, provider-rights or storage change; maximum 30 days",
        c switch {
            "DURABLE_SHARED_STORAGE"=>"Verify persistent shared storage and OS access controls across restart and instances",
            "MULTI_WORKER_RECOVERY"=>"Demonstrate lease recovery and exactly one committed result after worker loss on isolated deployment storage",
            "PROVIDER_PERMISSION"=>"Obtain scoped historical acquisition, retention and reuse rights; no purchase/login without authorization",
            "BACKUP_RESTORE"=>"Restore production-format backup into a new isolated target; verify hashes and replay without overwriting live data",
            "SECRET_HANDLING"=>"Review managed secret access and redacted logs; record only audit digest, never values",
            "OPERATIONAL_ALERTS"=>"Exercise failure/lease/revocation alerts and acknowledge delivery with authorized recipients",
            "ROLLBACK"=>"Rehearse prior signed-off artifact rollback and verify compatibility without database rollback",
            _=>"Obtain explicit scoped enable approval after the other seven gates; deployment does not grant approval"
        })).ToArray();
    // trustedReceiptDigests must come from an independently reviewed, OS-protected deployment acceptance ledger.
    // This validates evidence metadata; a digest alone is not verification or authorization to run a worker.
    public static ReadinessCheck[] AssessEvidence(DeploymentEvidence[] evidence,string environment,string sha,DateTimeOffset now,IReadOnlySet<string> trustedReceiptDigests)
    {
        if(evidence.Select(e=>e.Code).Distinct().Count()!=evidence.Length||evidence.Any(e=>!Phase4MOperations.RequiredChecks.Contains(e.Code)))throw new InvalidDataException("DEPLOYMENT_EVIDENCE_CODES_INVALID");
        return Phase4MOperations.RequiredChecks.Select(code=>{
            var e=evidence.SingleOrDefault(e=>e.Code==code);
            var pass=e is not null&&e.Environment==environment&&e.SourceCommit==sha&&e.Result=="PASS"&&e.EvidenceType=="REVIEWED_DEPLOYMENT_RECEIPT"&&
                e.EvidenceDigest is {Length:64}&&e.EvidenceDigest.All(Uri.IsHexDigit)&&e.EvidenceDigest==ResearchMath.Digest(e with{EvidenceDigest=null})&&trustedReceiptDigests.Contains(e.EvidenceDigest)&&
                e.VerifiedAt is not null&&e.VerifiedAt<=now&&e.VerifiedAt.Value.Offset==TimeSpan.Zero&&e.ExpiresAt is not null&&
                e.ExpiresAt>now&&e.ExpiresAt<=e.VerifiedAt.Value.AddDays(30)&&e.ExpiresAt.Value.Offset==TimeSpan.Zero&&
                !string.IsNullOrWhiteSpace(e.Reviewer)&&!string.IsNullOrWhiteSpace(e.RevalidationRule);
            return new ReadinessCheck(code,pass?"Verified":"Blocked",pass?e!.EvidenceDigest!:e?.RequiredNextAction??"MISSING_DEPLOYMENT_EVIDENCE");
        }).ToArray();
    }
    public static BaselineReadinessDecision Decide(TrendFactoryReport trend)
    {
        if(trend.Policy!=SamplePolicy.Phase4I)throw new InvalidDataException("TREND_SAMPLE_POLICY_CHANGED");
        DatasetStage Stage(string name)=>trend.Stages.Single(s=>s.Name==name);
        var authority=Stage("Authority");var dataset=Stage("Dataset Approval");var samples=Stage("Sample Sufficiency");
        var status=authority.Status!="VERIFIED"?"BLOCKED_BY_AUTHORITY":dataset.Status!="APPROVED"?"APPROVED_DATASET_REQUIRED":
            samples.Status=="INSUFFICIENT_SAMPLE"?"INSUFFICIENT_SAMPLE":samples.Status=="SUFFICIENT"&&trend.Baseline=="COMPLETED"?"READY":"NOT_RUN";
        return new(status,trend.Baseline,trend.Stages.SelectMany(s=>s.Reasons).Distinct().ToArray(),authority.Status,dataset.Status,samples.Status,
            trend.FormulaDigest,trend.Policy,trend.OosStatus,trend.Promotion,trend.Phase5);
    }
    public static ResolutionPacket[] Packets(Phase4IReport p,Phase4JReport e,Phase4MReport m)
    {
        Phase4MOperations.Validate(m,p,e);
        string[] priorities=["HistoricalPublication","RevisionBehavior","SourceIdentity","HistoricalInstrumentIdentity","TradingCalendar","HistoricalUniverse","CorporateAction","PriceBasis"];
        return e.Plan.Select(t=>{
            var c=m.Acquisition.Single(c=>c.TaskId==t.TaskId);var q=m.ReviewQueue.Single(q=>q.TaskId==t.TaskId);
            var impacts=e.Impacts.Where(i=>i.SourceVersion==t.SourceVersion&&i.SourceId==t.SourceId).Select(i=>i.Reason).Concat(q.Status=="REVOKED"?["REVIEW_REVOKED"]:Array.Empty<string>()).ToArray();
            var priority=Array.FindIndex(priorities,x=>t.ClaimKey.Contains(x,StringComparison.Ordinal));
            return new ResolutionPacket(t.TaskId,t.ClaimKey,t.SubjectDigest,t.Instruments,t.CoverageStart,t.CoverageEnd,[t.SourceVersion],c.RawDigests,
                c.PublishedAtEvidence,c.AvailableAtEvidence,c.RevisionEvidence,c.RightsStatus,c.ReviewReceipts.Concat(q.ReviewDigest is null?[]:new[]{q.ReviewDigest}).Distinct().ToArray(),
                t.BlockingReasons.Concat(q.BlockingReasons).Concat(impacts).Distinct().ToArray(),t.NextAction,string.IsNullOrWhiteSpace(q.Reviewer)||q.Reviewer=="UNASSIGNED"?null:q.Reviewer,impacts,priority<0?99:priority+1);
        }).OrderBy(p=>p.Priority).ThenBy(p=>p.ClaimId,StringComparer.Ordinal).ThenBy(p=>p.TaskId,StringComparer.Ordinal).ToArray();
    }
    public static Phase4NReport Create(Phase4IReport p,Phase4JReport e,Phase4MReport m,string sha,DateTimeOffset at)
    {
        Phase4MOperations.Validate(m,p,e);
        if(sha.Length!=40||!sha.All(Uri.IsHexDigit)||at.Offset!=TimeSpan.Zero)throw new InvalidDataException("PHASE4N_BINDING_INVALID");
        var evidence=PendingEvidence(sha);
        var acceptance=AcceptanceCodes.Select(c=>new AcceptanceCase(c,"BLOCKED", "Blocked",c is "ANONYMOUS_API_MVC" or "PUBLIC_MARKET_QUOTES"?
            "Run Verify-Phase4M with expected version/SHA after deployment; record dated live receipt separately":
            "Authorized isolated A/B accounts and human-run production acceptance required; no credentials or cookies in records")).ToArray();
        var r=new Phase4NReport("Phase4N-v1","3.2.3","4N",sha,p.Digest,e.Digest,m.Digest,at,"Verified at Export",EnvironmentName,evidence,acceptance,
            Phase4MOperations.Readiness("Disabled",AssessEvidence(evidence,EnvironmentName,sha,at,new HashSet<string>())),Packets(p,e,m),Decide(Phase4MOperations.Trend(p,e,m.ReviewQueue)),p.Counts,e.Builds,"");
        return r with{Digest=ResearchMath.Digest(r)};
    }
    public static void Validate(Phase4NReport r,Phase4IReport p,Phase4JReport e,Phase4MReport m)
    {
        var expected=Create(p,e,m,r.SourceCodeSha,r.ExportedAt);
        if(r.Digest!=ResearchMath.Digest(r with{Digest=""})||ResearchMath.Digest(r)!=ResearchMath.Digest(expected))throw new InvalidDataException("PHASE4N_EXPORT_INVALID");
    }
}
