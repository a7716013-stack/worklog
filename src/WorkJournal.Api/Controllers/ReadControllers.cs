using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Api.Contracts;
using WorkJournal.Api.Services;
using WorkJournal.Web.Services;
namespace WorkJournal.Api.Controllers;

[ApiController,Route("api/v1/system"),EnableRateLimiting("ReadPolicy")]
public sealed class SystemApiController(IApiReadiness readiness) : ControllerBase
{
    [HttpGet("health")] public ActionResult<HealthDto> Health()=>ReadMapping.Health();
    [HttpGet("readiness")] public async Task<ActionResult<ReadinessDto>> Readiness(CancellationToken ct) {
        var result=await readiness.Check(ct); return StatusCode(result.Status=="ready"?200:503,result);
    }
}
[ApiController,Route("api/v1/market"),EnableRateLimiting("ReadPolicy")]
public sealed class MarketApiController(IMarketRadarService radar,IMarketIndustries industries) : ControllerBase
{
    [HttpGet("radar")]
    public async Task<ActionResult<RadarDto>> Radar([FromQuery(Name="")] PageQuery page,string market="all",DateOnly? date=null,bool analyze=false,CancellationToken ct=default) {
        if(market is not ("all" or "twse" or "tpex" or "etf")) throw new ApiFault(400,"VALIDATION_ERROR");
        try {return ReadMapping.Radar(await radar.GetAsync(market,date,analyze,ct),page);}
        catch(Exception e) when(TaiwanMarketRankingProvider.Failure(e,ct)) {throw new ApiFault(503,"UPSTREAM_UNAVAILABLE");}
    }
    [HttpGet("industries")]
    public async Task<ActionResult<PageDto<IndustryDto>>> Industries([FromQuery(Name="")] PageQuery page,CancellationToken ct) {
        try {return PageDto<IndustryDto>.From(await industries.Get(ct),page);}
        catch(Exception e) when(TaiwanMarketRankingProvider.Failure(e,ct)) {throw new ApiFault(503,"UPSTREAM_UNAVAILABLE");}
    }
}
[ApiController,Route("api/v1/evidence"),EnableRateLimiting("ReadPolicy")]
public sealed class EvidenceApiController : ControllerBase
{
    [HttpGet("models")] public ActionResult<PageDto<ModelDto>> Models([FromQuery(Name="")] PageQuery page)=>PageDto<ModelDto>.From([ReadMapping.Model()],page);
    [HttpGet("models/{modelVersion}")] public ActionResult<ModelDto> Model(string modelVersion) {
        var model=ReadMapping.Model();return modelVersion==model.ModelVersion?model:NotFound();
    }
    [Authorize(Policy="ApiResearchRead"),HttpGet("{instrumentId}")]
    public ActionResult<ModelDto> Evidence(Guid instrumentId)=>throw new ApiFault(503,"DATA_NOT_READY");
}
[ApiController,Route("api/v1/authority"),Authorize(Policy="ApiAuthorityRead"),EnableRateLimiting("ReadPolicy")]
public sealed class AuthorityApiController(ResearchReadService research) : ControllerBase
{
    [HttpGet("domains")] public ActionResult<PageDto<DomainDto>> Domains([FromQuery(Name="")] PageQuery page)=>PageDto<DomainDto>.From(research.Domains(),page);
    [HttpGet("claims")] public ActionResult<PageDto<ClaimDto>> Claims([FromQuery(Name="")] PageQuery page)=>PageDto<ClaimDto>.From(research.Read().Claims,page);
    [HttpGet("claims/{claimId}")] public ActionResult<ClaimDto> Claim(string claimId)=>research.Read().Claims.FirstOrDefault(x=>x.ClaimId==claimId) is {} claim?claim:NotFound();
    [HttpGet("coverage")] public ActionResult<CoverageDto> Coverage()=>research.Read().Coverage;
}
[ApiController,Route("api/v1/research"),Authorize(Policy="ApiResearchRead"),EnableRateLimiting("ReadPolicy")]
public sealed class ResearchApiController(ResearchReadService research) : ControllerBase
{
    [HttpGet("runs")] public ActionResult<PageDto<RunDto>> Runs([FromQuery(Name="")] PageQuery page)=>PageDto<RunDto>.From(research.Read().Runs,page);
    [HttpGet("runs/{runId}")] public ActionResult<RunDto> Run(string runId)=>research.Read().Runs.FirstOrDefault(x=>x.RunId==runId) is {} run?run:NotFound();
    [HttpGet("runs/{runId}/metrics")] public ActionResult<MetricsDto> Metrics(string runId) {
        var run=research.Read().Runs.FirstOrDefault(x=>x.RunId==runId);if(run is null)return NotFound();
        if(run.Status!="NOT_RUN")throw new ApiFault(503,"DATA_NOT_READY");return new MetricsDto("NOT_RUN",null,null,null,null);
    }
    [HttpGet("dataset-approvals")] public ActionResult<PageDto<ApprovalDto>> Approvals([FromQuery(Name="")] PageQuery page)=>PageDto<ApprovalDto>.From(research.Read().Approvals,page);
    [HttpGet("dataset-approvals/{approvalId}")] public ActionResult<ApprovalDto> Approval(string approvalId)=>research.Read().Approvals.FirstOrDefault(x=>x.ApprovalId==approvalId) is {} approval?approval:NotFound();
}
[ApiController,Route("api/v1/paper"),Authorize(Policy="ApiPaperTrade"),EnableRateLimiting("ReadPolicy")]
public sealed class PaperApiController(IPaperTradingService paper) : ControllerBase
{
    [HttpGet("account")] public async Task<ActionResult<AccountDto>> Account(CancellationToken ct) {var a=await paper.GetAccountAsync(ct);return new AccountDto(a.Name,a.InitialCash,a.Cash,a.RealizedProfitLoss,a.UpdatedAt);}
    [HttpGet("positions")] public async Task<ActionResult<PageDto<PositionDto>>> Positions([FromQuery(Name="")] PageQuery page,CancellationToken ct) {
        var p=await paper.GetPortfolioAsync(ct,refreshDaily:false);
        return PageDto<PositionDto>.From(p.Positions.Select(x=>new PositionDto(x.Position.StockId,x.Position.Quantity,x.Position.CostBasis,x.CurrentPrice,x.MarketValue,x.UnrealizedProfitLoss)),page);
    }
    [HttpGet("orders")] public async Task<ActionResult<PageDto<OrderDto>>> Orders([FromQuery(Name="")] PageQuery page,CancellationToken ct)=>PageDto<OrderDto>.From((await paper.GetOrdersAsync(ct)).OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Select(x=>new OrderDto(x.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),x.ClientRequestId,x.StockId,x.Side.ToString().ToLowerInvariant(),x.OrderType.ToString().ToLowerInvariant(),x.Quantity,x.LimitPrice,x.Status.ToString(),x.CreatedAt,x.FilledPrice)),page);
    [HttpGet("trades")] public async Task<ActionResult<PageDto<TradeDto>>> Trades([FromQuery(Name="")] PageQuery page,CancellationToken ct)=>PageDto<TradeDto>.From((await paper.GetTradesAsync(ct)).OrderByDescending(x=>x.ExecutedAt).ThenByDescending(x=>x.Id).Select(x=>new TradeDto(x.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),x.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture),x.StockId,x.Side.ToString().ToLowerInvariant(),x.Quantity,x.Price,x.Commission,x.TransactionTax,x.NetCashAmount,x.QuoteTradeDate,x.ExecutedAt)),page);
}
