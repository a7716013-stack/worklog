using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record ReviewEvidenceRequirement(string Code,string Status,string RequiredEvidence,string? EvidenceDigest,string? Reviewer);
public sealed record SourceCompatibility(string Source,string Resource,string License,string Status,string[] CompatibleFields,string[] Gaps,string[] Sources);
public sealed record RightsDecisionPack(RightsEvidenceNote[] Purposes,SourceCompatibility[] Alternatives,string ExactEndpointStatus,string[] RequiredReviewerActions);
public sealed record PilotProposalReview(string Status,PilotScope Scope,int InstrumentCount,int MonthCount,int UniqueRequests,int MaximumAttempts,
    string InstrumentIdentity,string Calendar,string OperatorStatus,string[] RequiredConfirmations);
public sealed record EnvironmentAcceptanceReview(string Status,ReviewEvidenceRequirement[] Controls,ReviewEvidenceRequirement[] Boundaries,
    IsolatedEnvironmentReceipt? AcceptedReceipt,string EvidenceClass);
public sealed record ReadinessSeparation(string AcquisitionReady,string DatasetEligible,string ResearchReady,string[] BlockingReasons);
public sealed record CaptureVerificationReview(int RealRuns,int NewFacts,string ReplayStatus,ReviewEvidenceRequirement[] Requirements);
public sealed record Phase4RReport(string SchemaVersion,string Version,string SourceCodeSha,string ParentDigest,string[] InputVersions,DateTimeOffset ExportedAt,
    string EvidenceClass,RightsDecisionPack Rights,PilotProposalReview Proposal,EnvironmentAcceptanceReview Environment,AcquisitionRunConsent? RunConsent,
    AcquisitionReadiness Acquisition,ReadinessSeparation Readiness,PilotActualCounts Actual,PilotResult[] Runs,CaptureVerificationReview CaptureVerification,
    HistoricalCoverage[] Historical,PilotCoverage Pit,SamplePolicy SamplePolicy,ReadinessCheck[] Production,string GoogleAcceptance,string FormulaDigest,
    string SchedulerMode,string Baseline,string Oos,string Promotion,string Phase5,string Digest);

// Review projections only. All executable plans and authorization remain in the existing PilotScope/worker contracts.
public static class Phase4ROperations
{
    public static PilotScope DraftScope(string sha)
    {
        if(sha.Length!=40||!sha.All(Uri.IsHexDigit))throw new ArgumentException("SOURCE_SHA_REQUIRED");
        var old=Phase4QOperations.DraftScope(sha);
        var symbols=old.Instruments.Take(10).ToArray();
        var start=new DateOnly(2026,7,1);var end=new DateOnly(2026,9,30);
        var plans=old.Requests.Where(p=>symbols.Contains(p.Instrument)&&p.RequestDate>=start&&p.RequestDate<=end)
            .Select(p=>p with{CoverageStart=start,CoverageEnd=end,Id=""}).Select(p=>p with{Id=ForwardCaptureStore.PlanId(p)}).ToArray();
        var scope=old with{Id="",Instruments=symbols,HistoricalStart=start,HistoricalEnd=end,Requests=plans,MaxRequests=90,StorageLimitBytes=16L*1024*1024,RightsDigests=[]};
        scope=scope with{Id=PilotScopeRules.Id(scope)};PilotScopeRules.Validate(scope);return scope;
    }
    private static ReviewEvidenceRequirement Missing(string code,string evidence)=>new(code,"BLOCKED",evidence,null,null);
    public static EnvironmentAcceptanceReview EnvironmentChecklist()=>new("BLOCKED",
        PilotExecutionRules.Controls.Select(code=>Missing(code,code switch{
            "ENDPOINT_ALLOWLIST"=>"Exact approved plan URLs; deny altered host/path/query/instrument/date and non-HTTPS.",
            "DNS_TLS"=>"Accepted host resolution, public-address checks and TLS validation in the isolated environment.",
            "NO_REDIRECT_PROXY"=>"Redirect denied and proxy disabled; accepted transport configuration and isolated negative checks.",
            "REQUEST_QUOTA"=>"Exact 30 unique requests / 90 total attempts including retries, plus existing 20-attempt/hour ceiling.",
            "CONCURRENCY"=>"One active worker; concurrent execution denied using the same accepted storage root.",
            "DURABLE_RESERVATION"=>"Persist attempt reservation before request; verify consumed budget survives restart.",
            "CANCELLATION"=>"Cancelled execution stops admission/request/write; reservation remains accounted.",
            "TIMEOUT"=>"Bounded request timeout observed with controlled transport; timeout consumes attempt.",
            "RETRY"=>"At most three attempts per unique request; every retry revalidates rights/consent/quota.",
            "CIRCUIT_BREAKER"=>"Failure threshold opens circuit; no further network calls while open.",
            "EXCLUSIVE_LOCK"=>"Exclusive lease cannot be acquired concurrently across processes.",
            "CRASH_RECOVERY"=>"Interrupted reservation retained; completed request not downloaded again after restart.",
            "IMMUTABLE_ARCHIVE"=>"Existing archive/version cannot be overwritten; changed execution receipt rejected.",
            "DIGEST_VERIFICATION"=>"Raw bytes, manifest and execution evidence digests verified; tampering rejected.",
            "REPLAY"=>"Archived input replay reproducible without network and with identical lineage.",
            "SENSITIVE_DATA_FILTER"=>"Secrets, local roots and raw bodies excluded from public metadata/log evidence.",
            _=>"64 KiB response / 16 MiB total candidate store budget enforced before admission, including metadata."
        })).ToArray(),
        new[]{"ACL","STORAGE_ROOT","SOURCE_SHA","ENVIRONMENT_DIGEST","INDEPENDENT_REVIEWER","EXPIRY_REVOCATION","EVIDENCE_INTEGRITY"}
            .Select(code=>Missing(code,"Actual isolated evidence bound to existing IsolatedEnvironmentReceipt and externally pinned PilotTrust; no self-issued acceptance.")).ToArray(),
        null,"Acceptance checklist; no actual accepted environment");
    public static Phase4RReport Create(Phase4QReport parent,string sha,DateTimeOffset at)
    {
        if(parent.SchemaVersion!="Phase4Q-v1"||parent.Digest!=ResearchMath.Digest(parent with{Digest=""})||at.Offset!=TimeSpan.Zero)
            throw new InvalidDataException("PHASE4R_PARENT_INVALID");
        var scope=DraftScope(sha);
        var rights=Phase4QOperations.Rights().Select(x=>x with{ObservedOn=new(2026,11,24),Digest=""})
            .Select(x=>x with{Digest=ResearchMath.Digest(x)}).ToArray();
        var alternative=new SourceCompatibility("Government dataset 11549 / TWSE","https://www.twse.com.tw/exchangeReport/STOCK_DAY_ALL?response=open_data",
            "OGDL-1.0 per dataset metadata; exact downloaded bytes not inspected","COMPATIBILITY_REVIEW_ONLY_NOT_CONNECTED",
            ["trade date","instrument code/name","volume","turnover","OHLC","price change","transaction count"],
            ["Daily CSV differs from monthly STOCK_DAY JSON; existing parser cannot be reused unchanged.","Historical month retrieval and historical versions are unproven.","Publication/revision/identity/calendar evidence remains unproven.","New endpoint requires separately reviewed rights/scope/allowlist/parser; no connection in Phase4R."],
            ["https://data.gov.tw/dataset/11549","https://data.gov.tw/license"]);
        var decisions=new RightsDecisionPack(rights,[alternative],"UNKNOWN_BLOCKED",
            ["Archive applicable source terms/version and exact resource mapping; note digests are not raw-page digests.","Independently review ACQUIRE, STORE, RESEARCH and REDISTRIBUTE separately.","Bind existing CapturePermission to exact new plan IDs, purpose, retention, expiry and evidence; provision external trust."]);
        var proposal=new PilotProposalReview("PROPOSED_NOT_APPROVED",scope,scope.Instruments.Length,scope.Requests.Select(p=>p.RequestDate).Distinct().Count(),scope.Requests.Length,scope.MaxRequests,
            "UNVERIFIED_CANDIDATE_SYMBOLS_NOT_HISTORICAL_UNIVERSE","UNVERIFIED_NO_TRADING_DAY_COUNTS","UNASSIGNED_REQUIRES_HUMAN",
            ["10_CANDIDATE_SYMBOLS_AND_HISTORICAL_IDENTITY","2026_07_THROUGH_2026_09_CANDIDATE_MONTHS","30_UNIQUE_REQUESTS_90_TOTAL_ATTEMPTS",
             "64_KIB_RESPONSE_16_MIB_STORE_90_DAY_RETENTION","2026_12_01_TO_2026_12_08_UTC_CANDIDATE_WINDOW","OPERATOR_AND_INDEPENDENT_RIGHTS_REFERENCES",
             "ALL_EDITS_REQUIRE_NEW_PLAN_OR_SCOPE_DIGEST_AND_REVIEW","CANDIDATE_SCOPE_IS_NOT_DOWNLOAD_AUTHORIZATION"]);
        var capture=new CaptureVerificationReview(0,0,"NOT_RUN_NO_REAL_CAPTURE",
            new[]{"RAW_RESPONSE_DIGEST","IMMUTABLE_VERSION","OBSERVED_AT","RETRIEVED_AT","SOURCE_PUBLICATION_EVIDENCE","REVISION_HISTORY","INSTRUMENT_IDENTITY","TRADING_DATE","UNIT_NORMALIZATION","QUALITY_QUARANTINE","REPLAY_REPRODUCIBILITY","DATASET_LINEAGE"}
                .Select(code=>Missing(code,"No Phase4R real capture. Verify existing SourceArchive/CaptureRun/Quality/Replay evidence after separately authorized acquisition; ObservedAt/RetrievedAt never imply PublishedAt.")).ToArray());
        var coverage=parent.Pit with{ProposedScopeDenominator=null,BlockingReasons=["PRIOR_INSPECTED_ROWS_ARE_NOT_NEW_10_STOCK_3_MONTH_SCOPE","NEW_SCOPE_CALENDAR_UNIVERSE_DENOMINATOR_UNKNOWN","NO_NEW_REAL_CAPTURE","INDEPENDENT_PIT_AND_COMPONENT_APPROVAL_REQUIRED"]};
        var r=new Phase4RReport("Phase4R-v1","3.2.7",sha,parent.Digest,["Phase4Q-v1","PilotScope-v1","HistoricalNormalized-v1","TrendSample-v1"],at,
            "Review snapshot; not live acceptance",decisions,proposal,EnvironmentChecklist(),null,parent.Acquisition,
            new("BLOCKED","BLOCKED","BLOCKED",["NO_APPROVED_RIGHTS_SCOPE_ENVIRONMENT_OR_RUN_CONSENT","NO_APPROVED_DATASET","TEN_CANDIDATE_SYMBOLS_DO_NOT_PROVE_VALID_DAILY_SAMPLES","THREE_MONTHS_DO_NOT_PROVE_250_SESSION_LOOKBACK_OR_FOLD_OUTCOMES","SEPARATE_OOS_REQUIRED"]),
            new(0,0,0,0,0,0,0,0,0),[],capture,parent.Historical,coverage,parent.SamplePolicy,parent.Production,parent.GoogleAcceptance,parent.FormulaDigest,
            "Disabled","NOT_RUN","NOT_RUN","NOT_PROMOTED","LOCKED","");
        return r with{Digest=ResearchMath.Digest(r)};
    }
    public static void Validate(Phase4RReport report,Phase4QReport parent)
    {
        if(report.Digest!=ResearchMath.Digest(report with{Digest=""})||ResearchMath.Digest(report)!=ResearchMath.Digest(Create(parent,report.SourceCodeSha,report.ExportedAt)))
            throw new InvalidDataException("PHASE4R_EXPORT_CHANGED");
    }
}
