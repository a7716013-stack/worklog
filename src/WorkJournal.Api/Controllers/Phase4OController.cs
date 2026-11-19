using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Api.Contracts;
using WorkJournal.Api.Services;
using WorkJournal.Application.Research;
namespace WorkJournal.Api.Controllers;
[ApiController,Route("api/v1/data"),Authorize(Policy="ApiResearchRead"),Authorize(Policy="ApiAuthorityAdmin"),EnableRateLimiting("ReadPolicy")]
public sealed class Phase4OController(Phase4OReadAdapter service):ControllerBase
{
    [HttpGet("operations")]public ActionResult<Phase4OReport> Operations()=>service.Read();
    [HttpGet("captures")]public ActionResult<PageDto<CaptureRun>> Captures([FromQuery(Name="")]PageQuery page)=>PageDto<CaptureRun>.From(service.Read().Capture.Runs,page);
}
