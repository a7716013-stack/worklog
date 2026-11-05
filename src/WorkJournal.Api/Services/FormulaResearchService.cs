using WorkJournal.Domain.Research;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Services.Evidence;
namespace WorkJournal.Api.Services;
public sealed record ResearchStatusDto(string Evidence,string FormulaStatus,string DatasetStatus,string BaselineStatus,string ResearchStatus,string PromotionStatus);
public sealed record CandidateDto(string CandidateId,FormulaIdentity Identity,ResearchStatusDto[] Status,string[] Features,string V3Threshold,string Phase5);
public sealed record ResearchResourceDto<T>(string Status,T? Value,string[] BlockingReasons);
public sealed record ResearchPackageSummary(FormulaIdentity Identity,ResearchStatusDto[] Status,string[] ResourceReferences);
public sealed record CanonicalComputeRequest(CandidateDataset Dataset,Guid InstrumentId,V3EvidenceCandidateV1ParameterSet Parameters);
public sealed record CanonicalComputeResult(CalculationIdentity CalculationIdentity,CandidateTrace Trace,string ResearchStatus,bool ProductionEligible);
public sealed record AggregationRequest(NormalizedEvidence[] Evidence);
public sealed record AggregationResult(FormulaIdentity FormulaIdentity,EvidenceScore Score,string Status);

public sealed class FormulaResearchService(ResearchReadService state,ValidationReadAdapter validation)
{
    private static readonly Lazy<FormulaResearchPackage> empty=new(()=>FormulaDiagnostics.Build([],1,5,"NOT_RUN_NO_APPROVED_DATASET"));
    public FormulaResearchPackage Empty => empty.Value;
    public bool Known(string id)=>id==V3CandidateCalculator.Version;
    public ResearchStatusDto[] Status() => validation.Read().Components.Select(c=>new ResearchStatusDto(c.Evidence,"FROZEN_CANDIDATE",c.DatasetStatus,c.BaselineStatus,"NOT_RUN","NOT_ELIGIBLE")).ToArray();
    public ResearchResourceDto<FeatureValidationResult> Feature(string id)
    {
        var r=validation.Read();var value=r.Diagnostics.SelectMany(d=>d.Features).SingleOrDefault(f=>f.FeatureName==id);
        return value is null?new("NOT_RUN",null,["APPROVED_REAL_DATASET_REQUIRED"]):new("FEATURE_DIAGNOSTICS_ONLY",value,["FUTURE_OUTCOME_VALIDATION_NOT_RUN"]);
    }
    public ResearchResourceDto<EvidenceValidationResult> Evidence(string id)
    {
        var value=validation.Read().Diagnostics.SingleOrDefault(d=>d.Evidence==id)?.Validation;
        return value is null?new("NOT_RUN",null,["APPROVED_REAL_DATASET_REQUIRED"]):new("FEATURE_DIAGNOSTICS_ONLY",value,["FUTURE_OUTCOME_VALIDATION_NOT_RUN"]);
    }
    public CandidateDto Candidate()=>new(V3CandidateCalculator.Version,FormulaCatalog.Identity,Status(),CandidateFeatureCatalog.Create(Guid.Empty).Select(x=>x.Code).ToArray(),"UNRESOLVED","LOCKED");
    public void RequireUnapprovedSnapshot()
    {
        // Fail closed when this release's authority state changes. Never mislabel approved results as an empty baseline.
        if(state.Read().Approvals.Length!=0)throw new ApiFault(503,"DATA_NOT_READY");
    }
    public ResearchResourceDto<T> NotRun<T>() {RequireUnapprovedSnapshot();return new("NOT_RUN",default,["APPROVED_REAL_DATASET_REQUIRED"]);}
    public void RequireRun(string id) {if(!state.Read().Runs.Any(x=>x.RunId==id))throw new ApiFault(404,"RESOURCE_NOT_FOUND");RequireUnapprovedSnapshot();}
    public CanonicalComputeResult Compute(CanonicalComputeRequest request)
    {
        var trace=new V3CandidateCalculator().Calculate(request.Dataset,request.InstrumentId,request.Parameters);
        return new(FormulaCatalog.Identify(trace,ReadMapping.Health().CommitSha??"UNSTAMPED"),trace,"FORMULA_ONLY_NOT_REAL_VALIDATION",false);
    }
}
