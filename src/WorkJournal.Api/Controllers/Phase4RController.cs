using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Api.Services;
using WorkJournal.Application.Research;
namespace WorkJournal.Api.Controllers;
[ApiController,Route("api/v1/data/pilot"),Authorize(Policy="ApiResearchRead"),Authorize(Policy="ApiAuthorityAdmin"),EnableRateLimiting("ReadPolicy")]
public sealed class Phase4RController(Phase4RReadAdapter service):ControllerBase
{
    [HttpGet("review")]
    public ActionResult<Phase4RReport> Readiness(){Response.Headers.CacheControl="no-store";return service.Read();}
}
