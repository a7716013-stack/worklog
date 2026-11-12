using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Api.Contracts;
using WorkJournal.Api.Services;
using WorkJournal.Application.Research;
namespace WorkJournal.Api.Controllers;
public sealed record EtlQualityDto(string JobId,string Status,DataQualityReport? Quality);
public sealed record EtlHealthDto(string Status,DateTimeOffset VerifiedAt,int Jobs,int RealJobs,int TestJobs,int FailedJobs,int QuarantinedJobs,string Semantics);
[ApiController,Route("api/v1"),Authorize(Policy="ApiResearchRead"),EnableRateLimiting("ReadPolicy")]
public sealed class EtlApiController(EtlReadAdapter service):ControllerBase
{
    [HttpGet("data/providers")]
    public ActionResult<PageDto<ProviderCapabilities>> Providers([FromQuery(Name="")]PageQuery page)=>PageDto<ProviderCapabilities>.From(service.Read().Providers,page);
    [HttpGet("etl/jobs")]
    public ActionResult<PageDto<EtlJobReport>> Jobs([FromQuery(Name="")]PageQuery page,string? provider=null,string? status=null,string sort="started-desc")
    {
        var r=service.Read();if(provider is not null&&!r.Providers.Any(p=>p.Provider==provider)||status is not null&&status is not ("SUCCEEDED" or "FAILED" or "CANCELLED" or "QUARANTINED")||sort is not ("started-desc" or "id"))throw new ApiFault(400,"VALIDATION_ERROR");
        var q=r.Jobs.Where(j=>(provider is null||j.Request.Provider==provider)&&(status is null||j.Status==status));
        return PageDto<EtlJobReport>.From(sort=="id"?q.OrderBy(j=>j.JobId,StringComparer.Ordinal):q.OrderByDescending(j=>j.StartedAt).ThenBy(j=>j.JobId,StringComparer.Ordinal),page);
    }
    [HttpGet("etl/jobs/{id}")]public ActionResult<EtlJobReport> Job(string id)=>service.Job(id);
    [HttpGet("etl/jobs/{id}/quality")]public ActionResult<EtlQualityDto> Quality(string id){var j=service.Job(id);return new EtlQualityDto(j.JobId,j.Status,j.Quality);}
    [HttpGet("etl/batches/{id}/lineage")]public ActionResult<EtlLineage> Lineage(string id){var j=service.Job(id);return j.Lineage is null?NotFound():j.Lineage;}
    [HttpGet("etl/health")]public ActionResult<EtlHealthDto> Health(){var r=service.Read();return new EtlHealthDto("VERIFIED_EXPORT_SNAPSHOT",r.VerifiedAt,r.Jobs.Length,r.Jobs.Count(j=>!j.Synthetic),r.Jobs.Count(j=>j.Synthetic),r.Jobs.Count(j=>j.Status=="FAILED"),r.Jobs.Count(j=>j.Status=="QUARANTINED"),r.ReplaySemantics);}
}
