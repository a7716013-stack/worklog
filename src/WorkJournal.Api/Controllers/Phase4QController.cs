using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Api.Services;
using WorkJournal.Application.Research;
namespace WorkJournal.Api.Controllers;
[ApiController,Route("api/v1/data/pilot"),Authorize(Policy="ApiResearchRead"),Authorize(Policy="ApiAuthorityAdmin"),EnableRateLimiting("ReadPolicy")]
public sealed class Phase4QController(Phase4QReadAdapter service):ControllerBase
{
    [HttpGet("readiness")]
    public ActionResult<Phase4QReport> Readiness(){Response.Headers.CacheControl="no-store";return service.Read();}
}
