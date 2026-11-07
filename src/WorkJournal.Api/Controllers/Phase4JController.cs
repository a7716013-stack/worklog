using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Api.Contracts;
using WorkJournal.Api.Services;
using WorkJournal.Application.Research;
namespace WorkJournal.Api.Controllers;
public sealed record SourceCatalogDto(PageDto<SourceCapability> Sources,string ReportDigest,DateTimeOffset VerifiedAt,string Semantics);
public sealed record AcquisitionPlanDto(PageDto<AcquisitionWork> Tasks,string ReportDigest,int MaximumAttempts,int ProviderHourlyLimit,int MaximumBytes,int Concurrency);
public sealed record AcquisitionHistoryDto(string TaskId,AcquisitionAttemptView[] Attempts,string ReportDigest);
public sealed record ComponentBuildDto(DatasetBuildReport Build,SourceImpact[] Impacts,string ReportDigest);

[ApiController,Route("api/v1/authority"),Authorize(Policy="ApiAuthorityRead"),EnableRateLimiting("ReadPolicy")]
public sealed class Phase4JAuthorityApiController(Phase4JReadAdapter service):ControllerBase
{
    [HttpGet("sources")]
    public ActionResult<SourceCatalogDto> Sources([FromQuery(Name="")]PageQuery page)
    {var r=service.Read();return new SourceCatalogDto(PageDto<SourceCapability>.From(r.Sources,page),r.Digest,r.GeneratedAt,"Captured source identity; historical capabilities are independent of access. Snapshot verified at export.");}
    [HttpGet("acquisition-plan")]
    public ActionResult<AcquisitionPlanDto> Plan([FromQuery(Name="")]PageQuery page,string? claimKey=null)
    {
        var r=service.Read();if(claimKey is not null&&!r.Plan.Any(t=>t.ClaimKey==claimKey))throw new ApiFault(400,"VALIDATION_ERROR");
        return new AcquisitionPlanDto(PageDto<AcquisitionWork>.From(r.Plan.Where(t=>claimKey is null||t.ClaimKey==claimKey),page),r.Digest,
            AcquisitionSafety.MaximumAttempts,AcquisitionSafety.ProviderHourlyLimit,AcquisitionSafety.MaximumBytes,AcquisitionSafety.Concurrency);
    }
    [HttpGet("acquisition-attempts/{id}")]
    public ActionResult<AcquisitionHistoryDto> Attempts(string id)
    {var r=service.Read();return !r.Plan.Any(t=>t.TaskId==id)?NotFound():new AcquisitionHistoryDto(id,r.Attempts.Where(a=>a.TaskId==id).ToArray(),r.Digest);}
}
[ApiController,Route("api/v1/research"),Authorize(Policy="ApiResearchRead"),EnableRateLimiting("ReadPolicy")]
public sealed class Phase4JResearchApiController(Phase4JReadAdapter service):ControllerBase
{
    [HttpGet("components/{component}/readiness")]
    public ActionResult<ComponentBuildDto> Component(string component)
    {var r=service.Read();var b=r.Builds.SingleOrDefault(b=>b.Component==component);return b is null?NotFound():new ComponentBuildDto(b,r.Impacts.Where(i=>i.Components.Contains(component)).ToArray(),r.Digest);}
}
