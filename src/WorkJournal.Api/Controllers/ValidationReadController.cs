using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Application.Research;
using WorkJournal.Api.Contracts;
using WorkJournal.Api.Services;
namespace WorkJournal.Api.Controllers;
public sealed record EligibilitySummaryDto(string Status,DatasetReadiness Dataset,ComponentReadiness[] Components,string FullModelStatus,
    string FormulaDigest,string ParameterChecksum,string SourceReportDigest,string ReportDigest,DateTimeOffset CapturedAt,string V3Threshold,string Promotion,string Phase5);
public sealed record DatasetCoverageDto(DatasetReadiness Dataset,PageDto<PitEligibilityEntry> Observations,string SourceReportDigest,string ReportDigest);

[ApiController,Route("api/v1"),Authorize(Policy="ApiResearchRead"),EnableRateLimiting("ReadPolicy")]
public sealed class ValidationReadController(ValidationReadAdapter service) : ControllerBase
{
    [HttpGet("research/eligibility-summary")]
    public ActionResult<EligibilitySummaryDto> Summary()
    {
        var r=service.Read();return new EligibilitySummaryDto(r.Dataset.Status.ToString(),r.Dataset,r.Components,r.FullModelStatus,
            r.Provenance.FormulaDigest,r.Provenance.ParameterChecksum,r.SourceReportDigest,r.Digest,r.GeneratedAt,r.V3Threshold,r.Promotion,r.Phase5);
    }
    [HttpGet("research/runs/{runId}/provenance")]
    public ActionResult<ValidationProvenance> Provenance(string runId)
    {var r=service.Read();return runId==r.Provenance.RunId?r.Provenance:NotFound();}
    [HttpGet("pit/observations/{id}/eligibility")]
    public ActionResult<PitEligibilityEntry> Observation(string id)
    {var r=service.Read();return r.Observations.SingleOrDefault(o=>o.ObservationId==id) is {} o?o:NotFound();}
    [HttpGet("research/datasets/{id}/coverage")]
    public ActionResult<DatasetCoverageDto> Coverage(string id,[FromQuery(Name="")] PageQuery page,DateOnly? from=null,DateOnly? to=null)
    {
        if(from.HasValue&&to.HasValue&&from>to)throw new ApiFault(400,"VALIDATION_ERROR");
        var r=service.Read();if(id!=r.Dataset.DatasetSnapshotId)return NotFound();
        var rows=r.Observations.Where(o=>(!from.HasValue||o.TradeDate>=from)&&(!to.HasValue||o.TradeDate<=to)).OrderBy(o=>o.TradeDate).ThenBy(o=>o.ObservationId,StringComparer.Ordinal);
        return new DatasetCoverageDto(r.Dataset,PageDto<PitEligibilityEntry>.From(rows,page),r.SourceReportDigest,r.Digest);
    }
}
