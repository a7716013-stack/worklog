using Microsoft.AspNetCore.Mvc;
using WorkJournal.Web.Services;
namespace WorkJournal.Web.Controllers;
public partial class StockAnalysisController
{
    [HttpGet]
    public async Task<IActionResult> ResearchData(string symbol,CancellationToken ct)
    {
        if(!ValidSwingSymbol(symbol))return BadRequest();
        try {return Json(StockResearchCalculator.Calculate(await stocks.SwingHistoryAsync(symbol,false,ct)));}
        catch(Exception e) when(SwingFailure(e,ct)){return StatusCode(503,new{message="走勢及法人資料暫時無法取得，請重試。"});}
    }
    [HttpGet]
    public async Task<IActionResult> StockRadar(string symbol,[FromServices] IMarketRadarService radar,[FromServices] IMarketRadarHistoryProvider history,CancellationToken ct)
    {
        if(!ValidSwingSymbol(symbol))return BadRequest();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try {
            var data=await radar.GetAsync("all",null,false,timeout.Token);
            var quote=data.Quotes.SingleOrDefault(x=>x.Stock.Symbol==symbol);
            var events=data.Events.Where(x=>x.StockId==symbol).OrderByDescending(x=>x.PublishedAt).ToArray();
            if(quote==null)return Json(new{score=(object?)null,events,warnings=data.Warnings,updatedAt=data.UpdatedAt,message="目前沒有可評分的市場收盤行情。"});
            WorkJournal.Web.Models.RadarHistory bars;
            try { bars=await history.GetAsync(quote,timeout.Token); }
            catch(Exception e) when(!ct.IsCancellationRequested&&(TaiwanMarketRankingProvider.Failure(e,ct)||e is OperationCanceledException))
            { bars=new([],[],null,null); } // Preserve successfully loaded announcements if history is unavailable.
            var score=MarketRadarScoreCalculator.Calculate(quote,bars,data.Events);
            return Json(new{score,events,warnings=data.Warnings,updatedAt=data.UpdatedAt,message="Radar V1；缺少資料不換算完整分數。"});
        } catch(Exception e) when(!ct.IsCancellationRequested&&(TaiwanMarketRankingProvider.Failure(e,ct)||e is OperationCanceledException))
        {return StatusCode(503,new{message="Radar／事件來源暫時無法取得，請重試。"});}
    }
}
