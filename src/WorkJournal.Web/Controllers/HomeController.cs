using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WorkJournal.Web.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using WorkJournal.Web.Services;
using WorkJournal.Web.ViewModels;

namespace WorkJournal.Web.Controllers;

public class HomeController : Controller
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index([FromServices] HomeDashboardService dashboard, CancellationToken ct)
    {
        var model = new HomeDashboardViewModel { CurrentDate = HomeDashboardService.Today,
            IsAuthenticated = User.Identity?.IsAuthenticated == true,
            UserDisplayName = User.FindFirstValue("display_name") ?? User.Identity?.Name ?? "" };
        if (model.IsAuthenticated && User.FindFirstValue(ClaimTypes.NameIdentifier) is string userId)
        {
            model.WorkSummary = await dashboard.WorkAsync(userId, model.CurrentDate, ct);
            model.InvestmentSummary = await dashboard.InvestmentAsync(userId, ct);
        }
        return View(model);
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> MarketSummary([FromServices] HomeDashboardService dashboard, [FromServices] IMarketRadarService radar, CancellationToken ct)
    {
        var model = new HomeDashboardViewModel();
        model.RadarSummary = await dashboard.RadarAsync(ct);
        model.MarketSummary = await dashboard.MarketAsync(radar, ct);
        return PartialView("_MarketSummary", model);
    }

    [Authorize, HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CalendarSummary([FromServices] HomeDashboardService dashboard, [FromServices] CalendarAggregationService calendar, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();
        var result = await dashboard.CalendarAsync(userId, HomeDashboardService.Today, calendar, ct);
        return PartialView("_CalendarSummary", new HomeDashboardViewModel { CalendarSummary = result });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
