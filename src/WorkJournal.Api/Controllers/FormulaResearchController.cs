using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Api.Contracts;
using WorkJournal.Api.Services;
using WorkJournal.Domain.Research;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Services.Evidence;
namespace WorkJournal.Api.Controllers;

[ApiController,Route("api/v1/research"),Authorize(Policy="ApiResearchRead"),EnableRateLimiting("ReadPolicy")]
public sealed class FormulaResearchController(FormulaResearchService research) : ControllerBase
{
    [HttpGet("features")] public ActionResult<PageDto<FeatureDefinition>> Features([FromQuery(Name="")] PageQuery page)=>PageDto<FeatureDefinition>.From(CandidateFeatureCatalog.Create(Guid.Empty),page);
    [HttpGet("features/{featureId}")] public ActionResult<FeatureDefinition> Feature(string featureId)=>CandidateFeatureCatalog.Create(Guid.Empty).FirstOrDefault(x=>x.Code==featureId) is {} value?value:NotFound();
    [HttpGet("features/{featureId}/validation")] public ActionResult<ResearchResourceDto<FeatureValidationResult>> FeatureValidation(string featureId) {
        if(!research.Candidate().Features.Contains(featureId))return NotFound();return research.Feature(featureId);
    }
    [HttpGet("evidence/{evidenceId}/validation")] public ActionResult<ResearchResourceDto<EvidenceValidationResult>> EvidenceValidation(string evidenceId) {
        if(!FullEvidenceModel.Weights.Keys.Any(x=>x.ToString()==evidenceId))return NotFound();return research.Evidence(evidenceId);
    }
    [HttpGet("candidates")] public ActionResult<PageDto<CandidateDto>> Candidates([FromQuery(Name="")] PageQuery page)=>PageDto<CandidateDto>.From([research.Candidate()],page);
    [HttpGet("candidates/{candidateId}")] public ActionResult<CandidateDto> Candidate(string candidateId)=>research.Known(candidateId)?research.Candidate():NotFound();
    [HttpGet("candidate-comparisons/{comparisonId}")] public ActionResult<CandidateComparison> Comparison(string comparisonId)=>FormulaGovernanceArchive.Read().Comparisons.SingleOrDefault(x=>x.ComparisonId==comparisonId) is {} item?item:NotFound();
    [HttpGet("hypotheses")] public ActionResult<PageDto<CandidateHypothesis>> Hypotheses([FromQuery(Name="")] PageQuery page)=>PageDto<CandidateHypothesis>.From(FormulaGovernanceArchive.Read().Hypotheses,page);
    [HttpGet("hypotheses/{id}")] public ActionResult<CandidateHypothesis> Hypothesis(string id)=>FormulaGovernanceArchive.Read().Hypotheses.SingleOrDefault(x=>x.CandidateHypothesisId==id) is {} item?item:NotFound();
    [HttpGet("calibrations")] public ActionResult<PageDto<CalibrationReceipt>> Calibrations([FromQuery(Name="")] PageQuery page)=>PageDto<CalibrationReceipt>.From(FormulaGovernanceArchive.Read().Calibrations,page);
    [HttpGet("calibrations/{id}")] public ActionResult<CalibrationReceipt> Calibration(string id)=>FormulaGovernanceArchive.Read().Calibrations.SingleOrDefault(x=>x.Id==id) is {} item?item:NotFound();
    [HttpGet("runs/{id}/ablations")] public ActionResult<ResearchResourceDto<PageDto<AblationDiagnostic>>> Ablations(string id,[FromQuery(Name="")] PageQuery page) {research.RequireRun(id);return research.NotRun<PageDto<AblationDiagnostic>>();}
    [HttpGet("runs/{id}/correlations")] public ActionResult<ResearchResourceDto<PageDto<CorrelationDiagnostic>>> Correlations(string id,[FromQuery(Name="")] PageQuery page) {research.RequireRun(id);return research.NotRun<PageDto<CorrelationDiagnostic>>();}
    [HttpGet("runs/{id}/stability")] public ActionResult<ResearchResourceDto<PageDto<StabilityDiagnostic>>> Stability(string id,[FromQuery(Name="")] PageQuery page,string period="year") {if(period is not ("year" or "quarter"))throw new ApiFault(400,"VALIDATION_ERROR");research.RequireRun(id);return research.NotRun<PageDto<StabilityDiagnostic>>();}
}

[ApiController,Route("api/v1/models"),Authorize(Policy="ApiResearchRead"),EnableRateLimiting("ReadPolicy")]
public sealed class FormulaModelController(FormulaResearchService research) : ControllerBase
{
    private void Require(string id) {if(!research.Known(id))throw new ApiFault(404,"RESOURCE_NOT_FOUND");}
    [HttpGet("{id}/definition")] public ActionResult<CandidateDto> Definition(string id) {Require(id);return research.Candidate();}
    [HttpGet("{id}/parameters")] public ActionResult<V3EvidenceCandidateV1ParameterSet> Parameters(string id) {Require(id);return new V3EvidenceCandidateV1ParameterSet();}
    [HttpGet("{id}/validation")] public ActionResult<ResearchResourceDto<FullEvidenceValidationResult>> Validation(string id) {Require(id);return research.NotRun<FullEvidenceValidationResult>();}
    [HttpGet("{id}/promotion-status")] public ActionResult<ResearchStatusDto[]> Promotion(string id) {Require(id);return research.Status();}
    [HttpGet("{id}/promotion-review")] public ActionResult<ResearchResourceDto<string>> Review(string id) {Require(id);return research.NotRun<string>();}
    [HttpGet("{id}/freeze-receipt")] public ActionResult<ResearchResourceDto<CandidateFreezeReceipt>> Freeze(string id) {Require(id);return research.NotRun<CandidateFreezeReceipt>();}
    [HttpGet("{id}/research-package")] public ActionResult<ResearchPackageSummary> Package(string id) {
        Require(id);return new ResearchPackageSummary(FormulaCatalog.Identity,research.Status(),["/api/v1/research/features","/api/v1/research/candidates/"+id,"/api/v1/models/"+id+"/validation","/api/v1/authority/coverage"]);
    }
    [HttpPost("{id}/compute"),ValidateAntiForgeryToken,RequestSizeLimit(4_000_000),EnableRateLimiting("ExpensiveResearchPolicy")]
    public ActionResult<CanonicalComputeResult> Compute(string id,[FromBody] CanonicalComputeRequest request) {
        Require(id);
        if(request.Dataset is null||request.Parameters is null||request.Dataset.Series.IsDefault||request.Dataset.Series.Length>500||request.Dataset.Series.Any(x=>x is null||x.Bars.IsDefault||x.Flows.IsDefault||x.Bars.Length>2000||x.Flows.Length>2000||x.Bars.Any(b=>b is null)||x.Flows.Any(f=>f is null)))throw new ApiFault(400,"VALIDATION_ERROR");
        try {return research.Compute(request);}catch(ArgumentException){throw new ApiFault(400,"VALIDATION_ERROR");}
    }
    [HttpPost("{id}/aggregate"),ValidateAntiForgeryToken,RequestSizeLimit(16_384)]
    public ActionResult<AggregationResult> Aggregate(string id,[FromBody] AggregationRequest request)
    {
        Require(id);
        try {return new AggregationResult(FormulaCatalog.Identity,new FullEvidenceScoreCalculator().Calculate(request.Evidence),"FORMULA_ONLY_NOT_REAL_VALIDATION");}
        catch(ArgumentException){throw new ApiFault(400,"VALIDATION_ERROR");}
    }
}
