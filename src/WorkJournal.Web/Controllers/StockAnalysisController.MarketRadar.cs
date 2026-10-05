using Microsoft.AspNetCore.Mvc;
using WorkJournal.Web.Services;
namespace WorkJournal.Web.Controllers;
public partial class StockAnalysisController
{
    [HttpGet, ResponseCache(Duration=0,Location=ResponseCacheLocation.None,NoStore=true)]
    public IActionResult MarketRadar() => View();

    [HttpGet, ResponseCache(Duration=0,Location=ResponseCacheLocation.None,NoStore=true)]
    public async Task<IActionResult> MarketRadarData([FromServices] IMarketRadarService radar,
        string market="all",DateOnly? date=null,bool analyze=false,CancellationToken cancellationToken=default)
    {
        if(!ModelState.IsValid || market is not ("all" or "twse" or "tpex" or "etf")) return BadRequest(new {message="市場或日期格式不正確。"});
        try { return Json(await radar.GetAsync(market,date,analyze,cancellationToken)); }
        catch(Exception e) when(TaiwanMarketRankingProvider.Failure(e,cancellationToken))
        { return StatusCode(503,new {message="市場資料暫時無法取得，請稍後再試。"}); }
    }
}
