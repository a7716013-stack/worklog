using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using WorkJournal.Web.Services;
using WorkJournal.Web.Security;
namespace WorkJournal.Web.Controllers;
public partial class StockAnalysisController
{
 [HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
 public async Task<IActionResult> MarketPulse(string market,[FromServices] MarketPulseService service,CancellationToken ct){
  market=string.IsNullOrEmpty(market)?"all":market;if(market is not ("all" or "twse" or "tpex"))return BadRequest();
  try{return Json(await service.OverviewAsync(market,ct));}catch(Exception e) when(SwingFailure(e,ct)){return StatusCode(503,new{message="市場來源暫時無法取得，請稍後重試。"});}
 }
 [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
 public async Task<IActionResult> PortfolioHistory([FromServices] PortfolioAnalyticsService service,[FromServices] ICurrentUser user,CancellationToken ct)=>Json(await service.HistoryAsync(user.Id,ct));
}
