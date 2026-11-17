using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Api.Contracts;
using WorkJournal.Api.Services;
using WorkJournal.Application.Research;
namespace WorkJournal.Api.Controllers;
public sealed record OperationsDashboardDto(string Version,string Phase,string SourceCodeSha,string SnapshotDigest,DateTimeOffset ExportedAt,string EvidenceClass,
    string Environment,DeploymentEvidence[] DeploymentEvidence,AcceptanceCase[] ProductionAcceptance,ProductionReadiness Production,
    BaselineReadinessDecision Baseline,ResearchCounts Counts,DatasetBuildReport[] Datasets,int ManualActionCount);
[ApiController,Route("api/v1"),Authorize(Policy="ApiResearchRead"),Authorize(Policy="ApiAuthorityAdmin"),EnableRateLimiting("ReadPolicy")]
public sealed class Phase4NController(Phase4NReadAdapter service):ControllerBase
{
    [HttpGet("operations/dashboard")]
    public ActionResult<OperationsDashboardDto> Dashboard(){var r=service.Read();return new OperationsDashboardDto(r.Version,r.Phase,r.SourceCodeSha,r.Digest,r.ExportedAt,r.EvidenceClass,r.Environment,r.DeploymentEvidence,r.ProductionAcceptance,r.Production,r.Baseline,r.Counts,r.Datasets,r.Packets.Length);}
    [HttpGet("authority/resolution-packets")]
    public ActionResult<PageDto<ResolutionPacket>> Packets([FromQuery(Name="")]PageQuery page)=>PageDto<ResolutionPacket>.From(service.Read().Packets,page);
}
