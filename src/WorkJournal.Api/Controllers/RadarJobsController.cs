using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Web.Services;
using WorkJournal.Web.Models;
namespace WorkJournal.Api.Controllers;

[ApiController,Route("api/v1/market"),EnableRateLimiting("ExpensiveResearchPolicy")]
public sealed class RadarJobsController(RadarJobs jobs,IMarketRadarService radar) : ControllerBase
{
    // Bounded daily universe (about 2,500 rows); preserves the website's complete-market filters.
    [HttpGet("radar/snapshot")]
    public async Task<ActionResult<MarketRadarResult>> Snapshot(string market="all",DateOnly? date=null,CancellationToken ct=default)
    {
        if(market is not ("all" or "twse" or "tpex" or "etf"))throw new ApiFault(400,"VALIDATION_ERROR");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try{return await radar.GetAsync(market,date,false,deadline.Token).WaitAsync(deadline.Token);}
        catch(Exception e) when(MarketProviderFailure.IsExpected(e,ct)){throw new ApiFault(503,"UPSTREAM_UNAVAILABLE");}
    }
    [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing), HttpPost("radar/jobs"),ValidateAntiForgeryToken]
    public ActionResult<RadarJobView> Start(string market="all",DateOnly? date=null)
    {
        try {var view=jobs.Start(market,date);Response.Headers.RetryAfter="2";return Accepted($"/api/v1/market/radar/jobs/{view.JobId}",view);}
        catch(ArgumentException){throw new ApiFault(400,"VALIDATION_ERROR");}
        catch(InvalidOperationException){throw new ApiFault(429,"RATE_LIMITED");}
    }
    [WorkJournal.Web.Security.FeatureAccess(WorkJournal.Web.Security.FeaturePolicies.Swing), HttpGet("radar/jobs/{id}")]
    public ActionResult<RadarJobView> Status(string id)
    {Response.Headers.RetryAfter="2";return jobs.Get(id) is {} job?job:NotFound();}
}
