using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Api.Services;
using WorkJournal.Application.Research;
namespace WorkJournal.Api.Controllers;
[ApiController,Route("api/v1/data"),Authorize(Policy="ApiResearchRead"),Authorize(Policy="ApiAuthorityAdmin"),EnableRateLimiting("ReadPolicy")]
public sealed class Phase4PController(Phase4PReadAdapter service):ControllerBase
{
    [HttpGet("pilot")]
    public ActionResult<Phase4PReport> Pilot(){Response.Headers.CacheControl="no-store";return service.Read();}
}
