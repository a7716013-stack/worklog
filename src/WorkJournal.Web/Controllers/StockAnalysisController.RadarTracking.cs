using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkJournal.Web.Security;
using WorkJournal.Web.Services;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Controllers;
public partial class StockAnalysisController
{
    [Authorize,HttpGet]
    public IActionResult RadarModelValidation()=>Redirect(Url.Action(nameof(RadarPerformance))+Request.QueryString+"#model-validation");
    [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> RadarTracking([FromServices] MarketRadarStatisticsService stats,[FromServices] ICurrentUser user,[FromQuery] RadarTrackingFilter filter,CancellationToken ct)
    {
        if(!ModelState.IsValid)return BadRequest("篩選條件格式不正確。");
        try{return View(await stats.TrackingAsync(filter,user.Id,ct));}catch(ValidationException e){return BadRequest(e.Message);}
    }
    [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> RadarPerformance([FromServices] MarketRadarStatisticsService stats,[FromServices] ICurrentUser user,[FromQuery] RadarTrackingFilter filter,CancellationToken ct)
    {
        if(!ModelState.IsValid)return BadRequest("篩選條件格式不正確。");
        try{return View(await stats.StatisticsAsync(filter,user.Id,ct));}catch(ValidationException e){return BadRequest(e.Message);}
    }
    [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> RadarPaperComparison([FromServices] RadarPaperTradingComparisonService service,[FromServices] ICurrentUser user,CancellationToken ct)
        =>View(await service.CompareAsync(user.Id,ct));
    [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> RadarSnapshot(long id,[FromServices] RadarPaperTradingComparisonService service,[FromServices] ICurrentUser user,CancellationToken ct)
    {var row=await service.AccessibleAsync(id,user.Id,ct);return row==null?NotFound():View(row);}
    [Authorize,HttpPost]
    public async Task<IActionResult> CaptureMarketRadar([FromServices] MarketRadarRecommendationService service,CancellationToken ct)
    {TempData["Success"]=(await service.CaptureDailyAsync(ct)).Message;return RedirectToAction(nameof(RadarTracking));}
    [Authorize,HttpPost]
    public async Task<IActionResult> UpdateRadarPerformance([FromServices] MarketRadarPerformanceService service,CancellationToken ct)
    {TempData["Success"]=$"已檢查 {await service.UpdatePendingAsync(ct)} 筆推薦；未成熟期間維持待更新。";return RedirectToAction(nameof(RadarTracking));}
    [Authorize,HttpPost]
    public async Task<IActionResult> FollowRadarRecommendation(long? recommendationId,string? symbol,[FromServices] MarketRadarRecommendationService service,[FromServices] ICurrentUser user,CancellationToken ct)
    {
        if(!ModelState.IsValid)return BadRequest();
        try{await service.FollowAsync(user.Id,recommendationId,symbol,ct);TempData["Success"]="已加入個人追蹤；再次加入保留原始起算日。";}
        catch(ValidationException e){return BadRequest(e.Message);}
        return RedirectToAction(nameof(RadarTracking),new{Mine=true});
    }
    [Authorize,HttpPost]
    public async Task<IActionResult> AddPerformanceValidation(string symbol,string origin,
        [FromServices] MarketRadarRecommendationService service,[FromServices] IMarketRadarHistoryProvider history,
        [FromServices] ICurrentUser user,CancellationToken ct)
    {
        if(!ModelState.IsValid || origin is not ("paper" or "swing"))return BadRequest("來源或代號格式不正確。");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            await service.FollowPortfolioSymbolAsync(user.Id,symbol,origin,history,timeout.Token);
            TempData["Success"]="已加入個人績效模型驗證，沿用雷達 V1 快照；重複加入保留原始紀錄。未成熟期間顯示待更新。";
            return Redirect(Url.Action(nameof(RadarPerformance),new{Mine=true,Symbol=symbol,Basis="open"})+"#model-validation");
        }
        catch(ValidationException e){TempData["Warning"]=e.Message;}
        catch(Exception e) when(!ct.IsCancellationRequested && (TaiwanMarketRankingProvider.Failure(e,ct)||e is OperationCanceledException))
        {TempData["Warning"]="行情來源暫時無法取得，尚未加入績效模型驗證，請稍後重試。";}
        return RedirectToAction(origin=="paper"?nameof(PaperTrading):nameof(Swing));
    }
    [Authorize,HttpPost]
    public async Task<IActionResult> StopRadarTracking(long id,[FromServices] MarketRadarRecommendationService service,[FromServices] ICurrentUser user,CancellationToken ct)
    {if(!await service.StopAsync(user.Id,id,ct))return NotFound();TempData["Success"]="已停止更新個人績效，歷史紀錄保留。";return RedirectToAction(nameof(RadarTracking),new{Mine=true});}
}
