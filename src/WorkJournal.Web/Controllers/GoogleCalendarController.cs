using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WorkJournal.Web.Models;
using WorkJournal.Web.Security;
using WorkJournal.Web.Services;
using WorkJournal.Web.ViewModels;
using WorkJournal.Web.Data;
using Microsoft.EntityFrameworkCore;
namespace WorkJournal.Web.Controllers;

[Authorize]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class GoogleCalendarController(IGoogleCalendarService calendar, UserManager<ApplicationUser> users,
    IConfiguration configuration, CalendarSyncService sync, JournalDbContext db) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Authenticated user required.");
    [HttpGet]
    public async Task<IActionResult> Index(bool authorizationFailed = false)
    {
        ViewData["SyncLinks"] = await db.CalendarSyncLinks.AsNoTracking().Include(x => x.WorkLog)
            .Where(x => x.ApplicationUserId == UserId && !x.Retired).OrderByDescending(x => x.SyncedAt).ToListAsync(HttpContext.RequestAborted);
        if (authorizationFailed) ViewData["Warning"] = "Google 行事曆授權未完成。請使用登入網站的同一帳號，勾選事件權限並重新連結。";
        return View(await calendar.GetConnectionAsync(UserId, HttpContext.RequestAborted));
    }
    [HttpPost]
    public async Task<IActionResult> Sync(DateOnly month, int? resolveId = null, string? choice = null)
    {
        if (!ModelState.IsValid) { TempData["Warning"] = "請選擇有效月份。"; return RedirectToAction(nameof(Index)); }
        try { TempData["Success"] = await sync.RunAsync(UserId, month, resolveId, choice, HttpContext.RequestAborted); }
        catch (CalendarServiceException ex) { TempData["Warning"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
    [HttpPost]
    public async Task<IActionResult> Connect()
    {
        if (!GoogleAuthSettings.IsConfigured(configuration))
        { TempData["Warning"] = "管理員尚未完成 Google 登入設定。"; return RedirectToAction(nameof(Index)); }
        if (!string.Equals($"{Request.Scheme}://{Request.Host}", GoogleAuthSettings.Origin(configuration), StringComparison.OrdinalIgnoreCase))
            return Redirect(GoogleAuthSettings.Origin(configuration) + "/GoogleCalendar");
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var properties = new AuthenticationProperties { RedirectUri = "/GoogleCalendar" };
        properties.Items[GoogleAuthSettings.UserIdItem] = user.Id;
        properties.Items[GoogleAuthSettings.SecurityStampItem] = user.SecurityStamp;
        return Challenge(properties, GoogleAuthSettings.CalendarScheme);
    }
    // The Google OAuth middleware intercepts the registered callback before MVC.
    [HttpGet]
    public IActionResult OAuthCallback() => RedirectToAction(nameof(Index), new { authorizationFailed = true });
    [HttpPost]
    public async Task<IActionResult> Disconnect()
    {
        try
        {
            var revoked = await calendar.DisconnectAsync(UserId, HttpContext.RequestAborted);
            TempData[revoked ? "Success" : "Warning"] = revoked ? "已中斷 Google 行事曆連結。" :
                "網站已移除授權資料，但 Google 暫時無法撤銷授權；請至 Google 帳戶的第三方連線移除 WorkJournal。";
        }
        catch (CalendarServiceException ex) { TempData["Warning"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
    [HttpGet]
    public async Task<IActionResult> Events(DateOnly? month)
    {
        var selected = month ?? DateOnly.FromDateTime(DateTime.Today);
        selected = new DateOnly(Math.Clamp(selected.Year, 2000, 2100), selected.Month, 1);
        try { return Json(new { events = await calendar.GetEventsAsync(UserId,
            new(selected.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)),
            new(selected.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)), HttpContext.RequestAborted), warning = (string?)null }); }
        catch (CalendarServiceException ex) { return Json(new { events = Array.Empty<CalendarEventViewModel>(), warning = ex.Message }); }
    }
    [HttpGet]
    public IActionResult Create() => View("Edit", new CalendarEventInput());
    [HttpPost]
    public async Task<IActionResult> Create(CalendarEventInput input)
    {
        if (!ModelState.IsValid) return View("Edit", input);
        try { await calendar.CreateEventAsync(UserId, input, HttpContext.RequestAborted); return Saved(); }
        catch (CalendarServiceException ex) { ModelState.AddModelError("", ex.Message); return View("Edit", input); }
    }
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        try { ViewData["EventId"] = id; return View(CalendarEventInput.FromEvent(await calendar.GetEventAsync(UserId, id, HttpContext.RequestAborted))); }
        catch (CalendarServiceException ex) { return Failure(ex); }
    }
    [HttpPost]
    public async Task<IActionResult> Edit(string id, CalendarEventInput input)
    {
        ViewData["EventId"] = id;
        if (!ModelState.IsValid) return View(input);
        try { await calendar.UpdateEventAsync(UserId, id, input, HttpContext.RequestAborted); return Saved(); }
        catch (CalendarServiceException ex) { ModelState.AddModelError("", ex.Message); return View(input); }
    }
    [HttpGet]
    public async Task<IActionResult> Delete(string id)
    {
        try { return View(await calendar.GetEventAsync(UserId, id, HttpContext.RequestAborted)); }
        catch (CalendarServiceException ex) { return Failure(ex); }
    }
    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        try { await calendar.DeleteEventAsync(UserId, id, HttpContext.RequestAborted); return Saved(); }
        catch (CalendarServiceException ex) { return Failure(ex); }
    }
    private IActionResult Saved() { TempData["Success"] = "Google 行事曆已更新。"; return RedirectToAction("Index", "WorkLogs", new { }, "calendar"); }
    private IActionResult Failure(CalendarServiceException ex) { TempData["Warning"] = ex.Message; return RedirectToAction(nameof(Index)); }
}
