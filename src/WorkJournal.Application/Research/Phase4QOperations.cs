using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record RightsEvidenceNote(string Provider,string Dataset,string Purpose,string Status,string[] Sources,string SourceVersion,
    DateOnly ObservedOn,string Restriction,string EvidenceClass,string? Reviewer,DateTimeOffset? ExpiresAt,string[] PermissionDigests,string Digest);
public sealed record PilotReviewProposal(string Status,string Provider,string Dataset,string CaptureDataset,string InstrumentType,string[] Instruments,
    DateOnly HistoricalStart,DateOnly HistoricalEnd,DateOnly EvaluationStart,DateOnly EvaluationEnd,int TargetEvaluationSessions,int FeatureLookback,
    int OutcomeHorizon,DateOnly[] RequestMonths,int ExactRequestCount,int MaxRequests,long StorageLimitBytes,int MaximumResponseBytes,
    DateTimeOffset ForwardStart,DateTimeOffset ExpiresAt,int RetentionDays,string EndpointTemplate,string DraftScopeDigest,string CalendarStatus,
    string[] RightsReferences,string[] RequiredConfirmations);
public sealed record CoverageCell(string Gate,int Verified,int Inspected,decimal? Fraction,string Status);
public sealed record HistoricalCoverage(string Domain,string Status,int Supported,int Required,string[] BlockingReasons);
public sealed record PilotCoverage(int TotalRawFacts,int PriorRawFacts,int NewRawFacts,int InspectedObservations,int? ProposedScopeDenominator,
    int VerifiedHistoricalContext,int PublicationEvidenceObservations,int RevisionEvidenceObservations,int PitEligibleObservations,int ApprovedDatasetCount,
    CoverageCell[] SevenGates,string SourceDatasetVersion,string SourceDigest,string[] BlockingReasons);
public sealed record AcquisitionReadiness(string Status,bool CanAcquire,string[] RequiredConditions,string[] BlockingReasons);
public sealed record Phase4QReport(string SchemaVersion,string Version,string SourceCodeSha,string ParentDigest,string HistoricalDigest,DateTimeOffset ExportedAt,
    string EvidenceClass,RightsEvidenceNote[] Rights,PilotReviewProposal Proposal,AcquisitionReadiness Acquisition,ReadinessCheck[] IsolatedEnvironment,
    PilotActualCounts Actual,PilotResult[] Runs,HistoricalCoverage[] Historical,PilotCoverage Pit,SamplePolicy SamplePolicy,string TrendStatus,
    ReadinessCheck[] Production,string GoogleAcceptance,string FormulaDigest,string SchedulerMode,string Baseline,string Oos,string Promotion,string Phase5,string Digest);

public static class Phase4QOperations
{
    // Review candidates, not a reconstructed historical index or an approved universe.
    public static readonly string[] Symbols="1101 1102 1216 1301 1303 1326 1402 1476 1590 1605 2002 2105 2207 2301 2303 2308 2317 2327 2330 2345 2357 2379 2382 2395 2408 2409 2412 2454 2603 2609 2610 2615 2801 2880 2881 2882 2883 2884 2885 2886 2887 2890 2891 2892 2912 3008 3034 3037 3045 3711".Split(' ');
    public static readonly string[] GateNames=["HistoricalInstrumentIdentity","TradingCalendar","HistoricalUniverse","PriceVolume","Availability","Revision","PriceBasis"];
    public static PilotScope DraftScope(string sha)
    {
        var start=new DateOnly(2025,6,1);var end=new DateOnly(2026,11,30);
        var plans=Symbols.SelectMany(symbol=>Enumerable.Range(0,18).Select(n=>{
            var date=start.AddMonths(n);var p=new CapturePlan("","TWSE","Price",symbol,date,start,end,
                $"https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY?date={date:yyyyMMdd}&stockNo={symbol}&response=json",
                "HistoricalNormalized-v1",sha,false,90,"PROPOSED_90_DAYS_REQUIRES_STORE_APPROVAL");return p with{Id=ForwardCaptureStore.PlanId(p)};
        })).ToArray();
        var scope=new PilotScope("","TWSE",Symbols.ToArray(),["Price"],start,end,new(2026,12,1,0,0,0,TimeSpan.Zero),new(2026,12,8,0,0,0,TimeSpan.Zero),
            250,20,"TWSE",2700,1024L*1024*1024,65536,new(2026,12,8,0,0,0,TimeSpan.Zero),"UNASSIGNED_REQUIRES_HUMAN",plans,[]);
        scope=scope with{Id=PilotScopeRules.Id(scope)};PilotScopeRules.Validate(scope);return scope;
    }
    public static RightsEvidenceNote[] Rights()=>new[]{"ACQUIRE","STORE","RESEARCH","REDISTRIBUTE"}.Select(p=>{
        var restrictions=p switch{
            "ACQUIRE"=>"TWSE website clause 6 restricts automated downloads without the permitted method or consent. Dataset 11549 identifies a daily CSV resource under OGDL; applicability to monthly historical STOCK_DAY is unproven.",
            "STORE"=>"Website clause 8 limits reproduction subject to consent or specifically released open data. Retention of monthly historical responses requires exact scoped review; proposed retention is 90 days.",
            "RESEARCH"=>"OGDL permits reuse of covered open data with attribution, but exact historical endpoint coverage and independent purpose review are absent. Research references on a website are not a scoped grant.",
            _=>"Redistribution is independent. OGDL attribution obligations apply only to covered resources. No historical response redistribution receipt exists; website metadata excludes raw bodies."};
        var r=new RightsEvidenceNote("TWSE","DailyPrice / monthly STOCK_DAY",""+p,"UNKNOWN",
            ["https://www.twse.com.tw/zh/terms/use.html","https://data.gov.tw/dataset/11549","https://data.gov.tw/license"],
            "TWSE website: no explicit version; dataset metadata 2025-05-01; OGDL-1.0",new(2026,11,22),restrictions,
            "Source-discovery note; digest of this note, not original page bytes or an approval",null,null,[],"");return r with{Digest=ResearchMath.Digest(r)};
    }).ToArray();
    public static PilotCoverage Coverage(Phase4IReport historical)
    {
        Phase4IProjection.Validate(historical);var rows=historical.Validation.Observations;
        bool Pass(PitEligibilityEntry o,string gate)=>o.Gates.SingleOrDefault(g=>g.Gate==gate)?.Status is "SUPPORTED" or "AVAILABLE";
        var cells=GateNames.Select(g=>{var n=rows.Count(o=>Pass(o,g));return new CoverageCell(g,n,rows.Length,rows.Length==0?null:(decimal)n/rows.Length,n==rows.Length&&n>0?"VERIFIED_FOR_INSPECTED_ROWS":"BLOCKED");}).ToArray();
        return new(historical.Counts.TotalFacts,historical.Counts.TotalFacts,0,rows.Length,null,
            rows.Count(o=>new[]{"HistoricalInstrumentIdentity","TradingCalendar","HistoricalUniverse","PriceBasis"}.All(g=>Pass(o,g))),
            rows.Count(o=>Pass(o,"Availability")),rows.Count(o=>Pass(o,"Revision")),historical.Counts.EligibleObservations,
            historical.Validation.Components.Count(c=>c.ApprovalId is not null),cells,historical.Validation.Dataset.DatasetVersion,historical.Digest,
            ["INSPECTED_ROWS_ARE_PRIOR_EXPORT_NOT_PROPOSED_50_STOCK_SCOPE","PROPOSED_DENOMINATOR_UNKNOWN_PENDING_HISTORICAL_CALENDAR_UNIVERSE","NO_NEW_REAL_CAPTURE","INDEPENDENT_PIT_AND_COMPONENT_APPROVAL_REQUIRED"]);
    }
    public static Phase4QReport Create(Phase4PReport parent,Phase4IReport historical,string sha,DateTimeOffset at)
    {
        if(parent.SchemaVersion!="Phase4P-v1"||parent.Digest!=ResearchMath.Digest(parent with{Digest=""})||sha.Length!=40||!sha.All(Uri.IsHexDigit)||at.Offset!=TimeSpan.Zero)
            throw new InvalidDataException("PHASE4Q_PARENT_INVALID");
        var scope=DraftScope(sha);var coverage=Coverage(historical);
        var proposal=new PilotReviewProposal("PROPOSED_NOT_APPROVED","TWSE","DailyPrice","Price","PROPOSED_COMMON_STOCK_IDENTITY_UNVERIFIED",scope.Instruments,
            scope.HistoricalStart,scope.HistoricalEnd,new(2026,7,1),new(2026,9,30),60,scope.FeatureLookback,scope.OutcomeHorizon,
            scope.Requests.Select(p=>p.RequestDate).Distinct().Order().ToArray(),scope.Requests.Length,scope.MaxRequests,scope.StorageLimitBytes,scope.MaximumResponseBytes,
            scope.ForwardStart,scope.ExpiresAt,90,"https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY?date={yyyyMMdd}&stockNo={symbol}&response=json",scope.Id,
            "UNVERIFIED_TARGET_COUNTS_NOT_PROVEN_TRADING_DAYS",[],
            ["OPERATOR_IDENTITY","EXACT_INSTRUMENTS_AND_HISTORICAL_MEMBERSHIP","DATES_AND_60_EVALUATION_250_LOOKBACK_20_OUTCOME_SESSIONS","900_MONTHLY_REQUESTS_WITH_2700_TOTAL_ATTEMPT_CAP","1_GIB_STORAGE_64_KIB_RESPONSE_90_DAY_RETENTION","EXECUTION_WINDOW_UTC_AND_EXPIRY","INDEPENDENT_PURPOSE_RIGHTS_REFERENCES","ISOLATED_ENVIRONMENT_AND_EXPLICIT_RUN_CONSENT"]);
        var conditions=new[]{"INDEPENDENT_ACQUIRE_RIGHT","INDEPENDENT_STORE_RIGHT","EXACT_APPROVED_SCOPE","EXTERNALLY_PINNED_TRUST","ACCEPTED_ISOLATED_ENVIRONMENT","EXACT_QUOTA_DATE_INSTRUMENT_ENDPOINT","EXPLICIT_RUN_AUTHORIZATION"};
        var domains=new[]{"HistoricalPublication","RevisionBehavior","SourceIdentity","HistoricalInstrumentIdentity","HistoricalUniverse","TradingCalendar","CorporateActions","PriceBasis"};
        var history=domains.Select(d=>{
            var claims=historical.Validation.Claims.Where(c=>d switch{
                "HistoricalInstrumentIdentity" or "HistoricalUniverse" or "TradingCalendar"=>c.Domain==d,
                "CorporateActions"=>c.ClaimId=="CorporateActionHandling",_=>c.ClaimId==d}).ToArray();
            var supported=claims.Count(c=>c.CurrentStatus=="SUPPORTED"&&c.BlockingReasons.Length==0&&c.ResolutionReceiptDigest is not null);
            return new HistoricalCoverage(d,supported>0&&supported==claims.Length?"SUPPORTED_FOR_PRIOR_REVIEWED_SCOPE":"UNPROVEN",supported,claims.Length,
                claims.SelectMany(c=>c.BlockingReasons).Append("NEW_PILOT_SCOPE_REQUIRES_EXISTING_AUTHORITY_RESOLUTION_REVIEW").Distinct().ToArray());
        }).ToArray();
        var r=new Phase4QReport("Phase4Q-v1","3.2.6",sha,parent.Digest,historical.Digest,at,"Snapshot; engineering evidence is separate",
            Rights(),proposal,new("BLOCKED",false,conditions,conditions),PilotExecutionRules.Controls.Select(c=>new ReadinessCheck(c,"Blocked","NO_ACCEPTED_REAL_ISOLATED_ENVIRONMENT_RECEIPT")).ToArray(),
            new(0,0,0,0,0,0,0,0,0),[],history,coverage,SamplePolicy.Phase4I,"BLOCKED_NO_APPROVED_TREND_DATASET",
            Phase4MOperations.RequiredChecks.Select(c=>new ReadinessCheck(c,"Blocked","REAL_DEPLOYMENT_ACCEPTANCE_REQUIRED")).ToArray(),"BLOCKED_NO_ISOLATED_GOOGLE_A_B_ACCOUNTS",
            parent.FormulaDigest,"Disabled","NOT_RUN","NOT_RUN_SEPARATE_SELECTION_REQUIRED","NOT_PROMOTED","LOCKED","");
        return r with{Digest=ResearchMath.Digest(r)};
    }
    public static void Validate(Phase4QReport report,Phase4PReport parent,Phase4IReport historical)
    {if(report.Digest!=ResearchMath.Digest(report with{Digest=""})||ResearchMath.Digest(report)!=ResearchMath.Digest(Create(parent,historical,report.SourceCodeSha,report.ExportedAt)))throw new InvalidDataException("PHASE4Q_EXPORT_CHANGED");}
}
