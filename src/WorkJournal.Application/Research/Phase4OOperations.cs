using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;
public sealed record SourceRight(string Purpose,string Status,string? EvidenceDigest,string RequiredAction);
public sealed record ProviderPolicy(ProviderCapabilities Capabilities,string DatasetContract,string Coverage,string RateLimit,SourceRight[] Rights,
    string PublicationPolicy,string RevisionPolicy,string HealthStatus,string EvidenceClass,string Digest);
public sealed record HistoricalPilotPlan(string Status,int SuggestedMonths,DateOnly? Start,DateOnly? End,string[] Instruments,string[] CandidateProviders,
    string[] ExistingTaskIds,string[] RequiredApprovals,string[] Priorities,string Digest);
public sealed record CaptureSummary(string Status,string EvidenceClass,int NewRealSources,int NewRealRows,int NewHistoricalPublicationProofs,int NewRevisionProofs,
    int NewPitEligible,int NewApprovedDatasets,int ProductionGatesReleased,CaptureRun[] Runs,string[] BlockingReasons);
public sealed record WarehouseLayer(string Layer,string StorageModel,string Status,string Admission);
public sealed record Phase4OReport(string SchemaVersion,string Version,string Phase,string SourceCodeSha,string ParentDigest,DateTimeOffset ExportedAt,
    string EvidenceClass,ProviderPolicy[] Providers,HistoricalPilotPlan Acquisition,CaptureSummary Capture,WarehouseLayer[] Warehouse,
    string QualityStatus,string ReconciliationStatus,string SchedulerMode,string BaselineStatus,string FormulaDigest,string Promotion,string Phase5,string Digest);
public static class Phase4OOperations
{
    public static ProviderPolicy[] Providers()=>OfficialDataProviderAdapter.Catalog().Select(c=>{
        var policy=new ProviderPolicy(c,"Existing "+c.SchemaVersion+"; registered archive contract only",c.Provider=="FinMind"?"UNAPPROVED_CONTRACT":"Exact approved request scope only; historical coverage unproven",
            $"Local safety ceiling {AcquisitionSafety.ProviderHourlyLimit}/hour, concurrency {AcquisitionSafety.Concurrency}; not a provider entitlement",
            new[]{"ACQUIRE","STORE","RESEARCH","REDISTRIBUTE"}.Select(p=>new SourceRight(p,"UNKNOWN",null,"Independent scoped rights evidence and approval required; public accessibility alone is insufficient")).ToArray(),
            "UNKNOWN_ORIGINAL_PUBLICATION_POLICY","UNKNOWN_REVISION_POLICY","NOT_PROBED","Blocked","");return policy with{Digest=ResearchMath.Digest(policy)};
    }).ToArray();
    public static Phase4OReport Create(Phase4NReport previous,string sha,DateTimeOffset at)
    {
        if(previous.Digest!=ResearchMath.Digest(previous with{Digest=""})||previous.SchemaVersion!="Phase4N-v1"||sha.Length!=40||!sha.All(Uri.IsHexDigit)||at.Offset!=TimeSpan.Zero)throw new InvalidDataException("PHASE4O_BINDING_INVALID");
        var plan=new HistoricalPilotPlan("BLOCKED_SCOPE_AND_RIGHTS_APPROVAL_REQUIRED",3,null,null,[],["TWSE","TPEx","FinMind"],previous.Packets.Select(p=>p.TaskId).ToArray(),
            ["EXACT_DATES","INSTRUMENT_UNIVERSE","PROVIDER_DATASET_CONTRACT","ACQUISITION_RIGHTS","STORAGE_RETENTION_RIGHTS","RESEARCH_RIGHTS_BEFORE_RESEARCH","SEPARATE_REDISTRIBUTION_RIGHTS"],
            ["HistoricalPublication","RevisionBehavior","SourceIdentity","HistoricalInstrumentIdentity","TradingCalendar","HistoricalUniverse","CorporateAction","PriceBasis"],"");plan=plan with{Digest=ResearchMath.Digest(plan)};
        var capture=new CaptureSummary("BLOCKED","Blocked",0,0,0,0,0,0,0,[],["NO_APPROVED_PILOT_DATES_OR_INSTRUMENTS","UNKNOWN_ACQUISITION_AND_STORAGE_RIGHTS","PRODUCTION_SCHEDULER_DISABLED","NO_NEW_EXTERNAL_DOWNLOADS"]);
        var report=new Phase4OReport("Phase4O-v1","3.2.4","4O",sha,previous.Digest,at,"Snapshot",Providers(),plan,capture,
            [new("Bronze","SourceArchive + ForwardCaptureStore immutable observation receipts","NO_NEW_REAL_CAPTURE","Reviewed ACQUIRE/STORE rights and exact approved plan; retain original bytes and source time claims"),
             new("Silver","Existing EtlPipeline/EtlBatch/HistoricalFact plus CaptureQuality","NO_NEW_REAL_FACTS","Known schema and quality checks; mismatches quarantined, historical context remains unproven"),
             new("Gold","Existing independently scoped Authority/PIT/Dataset approval receipts","BLOCKED","Existing dataset approval only; no direct copy or new Gold approval mechanism")],
            "NOT_RUN_REAL_DATA","NOT_RUN_REAL_DATA",previous.Production.Mode,previous.Baseline.ExecutionStatus,previous.Baseline.FormulaDigest,previous.Baseline.Promotion,previous.Baseline.Phase5,"");
        return report with{Digest=ResearchMath.Digest(report)};
    }
    public static void Validate(Phase4OReport r,Phase4NReport parent)
    {if(r.Digest!=ResearchMath.Digest(r with{Digest=""})||ResearchMath.Digest(r)!=ResearchMath.Digest(Create(parent,r.SourceCodeSha,r.ExportedAt)))throw new InvalidDataException("PHASE4O_EXPORT_INVALID");}
}
