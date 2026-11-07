using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Api.Contracts;
using WorkJournal.Api.Services;
using WorkJournal.Application.Research;
namespace WorkJournal.Api.Controllers;
public sealed record ClosureQueueDto(PageDto<ClaimRequirement> Claims,string SourceReportDigest,string ReportDigest);
public sealed record ClaimEvidenceDto(ClaimRequirement Requirement,string SourceReportDigest,string ReportDigest);
public sealed record DatasetReadinessDto(DatasetReadiness Dataset,ResearchCounts Counts,ComponentReadiness[] Components,string SampleStatus,string SourceReportDigest,string ReportDigest,[property:ApiOptional]DatasetBuildReport[] Builds,[property:ApiOptional]string BuildReportDigest);
public sealed record ValidationSummaryDto(TrendValidationSummary Trend,ComponentReadiness[] Components,string FullModelStatus,string FormulaDigest,string ParameterChecksum,string SourceReportDigest,string ReportDigest);
public sealed record ReplayStatusDto(string RunId,EvidenceReplayStatus Replay,string ReportIntegrity,string SourceReportDigest,string ReportDigest);

[ApiController,Route("api/v1/authority"),Authorize(Policy="ApiAuthorityRead"),EnableRateLimiting("ReadPolicy")]
public sealed class AuthorityClosureApiController(Phase4IReadAdapter service):ControllerBase
{
    [HttpGet("closure-queue")]
    public ActionResult<ClosureQueueDto> Queue([FromQuery(Name="")]PageQuery page,string? domain=null)
    {
        var r=service.Read();var q=Phase4IProjection.Queue(r);
        if(domain is not null&&!q.Any(c=>c.Claim.Domain==domain))throw new ApiFault(400,"VALIDATION_ERROR");
        return new ClosureQueueDto(PageDto<ClaimRequirement>.From(q.Where(c=>domain is null||c.Claim.Domain==domain),page),r.Validation.SourceReportDigest,r.Digest);
    }
    [HttpGet("claims/{id}/evidence")]
    public ActionResult<ClaimEvidenceDto> Evidence(string id)
    {
        var r=service.Read();var matches=Phase4IProjection.Queue(r).Where(c=>c.Id==id||c.Claim.ClaimId==id).ToArray();
        if(matches.Length>1)throw new ApiFault(400,"AMBIGUOUS_CLAIM_ID");
        return matches.Length==0?NotFound():new ClaimEvidenceDto(matches[0],r.Validation.SourceReportDigest,r.Digest);
    }
}
[ApiController,Route("api/v1/research"),Authorize(Policy="ApiResearchRead"),EnableRateLimiting("ReadPolicy")]
public sealed class Phase4IResearchApiController(Phase4IReadAdapter service,Phase4JReadAdapter supply):ControllerBase
{
    [HttpGet("datasets/{id}/readiness")]
    public ActionResult<DatasetReadinessDto> Readiness(string id)
    {
        var r=service.Read();if(id!=r.Validation.Dataset.DatasetSnapshotId)return NotFound();var j=supply.Read();return new DatasetReadinessDto(r.Validation.Dataset,r.Counts,r.Validation.Components,r.Trend.Status,r.Validation.SourceReportDigest,r.Digest,j.Builds,j.Digest);
    }
    [HttpGet("runs/{id}/validation-summary")]
    public ActionResult<ValidationSummaryDto> Summary(string id)
    {
        var r=service.Read();return id!=r.Trend.RunId?NotFound():new ValidationSummaryDto(r.Trend,r.Validation.Components,r.Validation.FullModelStatus,r.Validation.Provenance.FormulaDigest,r.Validation.Provenance.ParameterChecksum,r.Validation.SourceReportDigest,r.Digest);
    }
    [HttpGet("runs/{id}/replay-status")]
    public ActionResult<ReplayStatusDto> Replay(string id)
    {
        var r=service.Read();return id!=r.Trend.RunId?NotFound():new ReplayStatusDto(id,r.Replay,"VERIFIED_ON_READ",r.Validation.SourceReportDigest,r.Digest);
    }
}
