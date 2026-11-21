using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;
public sealed record PilotActualCounts(int ApprovedProviderRights,int ApprovedPilotScopes,int RealAcquisitionRuns,int NewRawSnapshots,
    int NewNormalizedFacts,int HistoricalEvidenceAccepted,int PitEligibleObservations,int ApprovedDatasets,int ProductionGatesReleased);
public sealed record PilotProposal(string Status,int SuggestedInstruments,int SuggestedTradingDays,DateOnly? HistoricalStart,DateOnly? HistoricalEnd,
    string[] Instruments,string[] RequiredFields,string[] ApprovedRightsReferences);
public sealed record PilotReadinessSection(string Name,string Status,int? VerifiedCount,string[] Blockers);
public sealed record Phase4PReport(string SchemaVersion,string Version,string Phase,string SourceCodeSha,string ParentDigest,DateTimeOffset ExportedAt,
    string EvidenceClass,PilotActualCounts Counts,ProviderPolicy[] Providers,PilotProposal Scope,PilotResult[] Runs,PilotReadinessSection[] Coverage,
    SamplePolicy SamplePolicy,string FormulaDigest,string SchedulerMode,string Baseline,string Promotion,string Phase5,string[] Blockers,string Digest);
public static class Phase4POperations
{
    public static Phase4PReport Create(Phase4OReport parent,string sha,DateTimeOffset at)
    {
        if(parent.Digest!=ResearchMath.Digest(parent with{Digest=""})||parent.SchemaVersion!="Phase4O-v1"||sha.Length!=40||!sha.All(Uri.IsHexDigit)||at.Offset!=TimeSpan.Zero)
            throw new InvalidDataException("PHASE4P_PARENT_BINDING_INVALID");
        // This release has no approved external inputs. This export cannot promote fixtures into real evidence.
        var report=new Phase4PReport("Phase4P-v1","3.2.5","4P",sha,parent.Digest,at,"Snapshot",new(0,0,0,0,0,0,0,0,0),parent.Providers,
            new("PROPOSED_NOT_APPROVED",50,60,null,null,[],["Market","Instruments","DatasetTypes","HistoricalRange","ForwardRange","FeatureLookback","OutcomeHorizon","Provider","MaxRequests","StorageLimitBytes","Expiration","IndependentRights","IsolatedEnvironment"],[]),[],
            [new("HistoricalSecurityMaster","BLOCKED",null,["DATED_IDENTITY_SYMBOL_LISTING_DELISTING_MARKET_INDUSTRY_UNIVERSE_SUSPENSION_EVIDENCE_REQUIRED"]),
             new("TradingCalendar","BLOCKED",null,["VERIFIED_SESSIONS_HOLIDAYS_EMERGENCY_CLOSURES_REQUIRED"]),
             new("CorporateActions","BLOCKED",null,["SOURCE_BOUND_KNOWN_AT_ADJUSTMENT_VERSIONS_REQUIRED"]),
             new("FeatureOutcome","NOT_RUN",0,["RESEARCH_RIGHTS_AUTHORITY_PIT_DATASET_APPROVAL_REQUIRED"]),
             new("TrendSample","NOT_ASSESSED",0,["APPROVED_TREND_DATASET_REQUIRED","SEPARATE_OOS_SELECTION_REQUIRED"]),
             new("QualityReconciliation","NOT_RUN",0,["NO_REAL_CAPTURE"]),
             new("ProductionAcceptance","BLOCKED",0,Phase4MOperations.RequiredChecks.Concat(new[]{"ISOLATED_GOOGLE_A_B_ACCOUNTS"}).ToArray())],
            SamplePolicy.Phase4I,parent.FormulaDigest,"Disabled","NOT_RUN","NOT_PROMOTED","LOCKED",
            ["Independent reviewer must verify exact ACQUIRE/STORE/RESEARCH purpose receipts; REDISTRIBUTE remains separate",
             "Human must approve exact dates, instruments, lookback, horizon, request/storage budget and expiry",
             "Operator must provision externally pinned approvals and an authorized isolated durable environment",
             "Historical publication/revision and market-context evidence must pass existing Authority/PIT/dataset review",
             "Production Google A/B acceptance and eight deployment gates remain blocked; scheduler enable and real baseline require separate authorization"],"");
        return report with{Digest=ResearchMath.Digest(report)};
    }
    public static void Validate(Phase4PReport report,Phase4OReport parent)
    {if(report.Digest!=ResearchMath.Digest(report with{Digest=""})||ResearchMath.Digest(report)!=ResearchMath.Digest(Create(parent,report.SourceCodeSha,report.ExportedAt)))throw new InvalidDataException("PHASE4P_REPORT_CHANGED");}
}
