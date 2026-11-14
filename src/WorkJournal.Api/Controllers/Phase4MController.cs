using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Api.Contracts;
using WorkJournal.Api.Services;
using WorkJournal.Application.Research;
namespace WorkJournal.Api.Controllers;
public sealed record OperationsReadinessDto(string Version,string Phase,string SourceCodeSha,DateTimeOffset ExportedAt,string Semantics,ProductionReadiness Production,TrendFactoryReport Trend,EtlAlert[] Alerts,string Digest);
[ApiController,Route("api/v1"),Authorize(Policy="ApiResearchRead"),EnableRateLimiting("ReadPolicy")]
public sealed class Phase4MController(Phase4MReadAdapter service):ControllerBase
{
    [HttpGet("operations/readiness")]
    public ActionResult<OperationsReadinessDto> Readiness(){var r=service.Read();return new OperationsReadinessDto(r.Version,r.Phase,r.SourceCodeSha,r.ExportedAt,r.Semantics,r.Production,r.Trend,r.Alerts,r.Digest);}
    [HttpGet("research/trend/readiness")]
    public ActionResult<TrendFactoryReport> Trend()=>service.Read().Trend;
    [HttpGet("authority/workbench"),Authorize(Policy="ApiAuthorityAdmin")]
    public ActionResult<PageDto<ReviewQueueItem>> Queue([FromQuery(Name="")]PageQuery page)=>PageDto<ReviewQueueItem>.From(service.Read().ReviewQueue,page);
    [HttpGet("authority/acquisition-evidence"),Authorize(Policy="ApiAuthorityAdmin")]
    public ActionResult<PageDto<HistoricalEvidenceCard>> Acquisition([FromQuery(Name="")]PageQuery page)=>PageDto<HistoricalEvidenceCard>.From(service.Read().Acquisition,page);
}
