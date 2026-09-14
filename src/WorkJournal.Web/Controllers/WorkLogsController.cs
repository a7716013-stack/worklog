using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.ViewModels;

namespace WorkJournal.Web.Controllers;

public class WorkLogsController(JournalDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(WorkLogIndexViewModel filter)
    {
        if (filter.Status.HasValue && !Enum.IsDefined(filter.Status.Value))
            ModelState.AddModelError(nameof(filter.Status), "請選擇有效的進度。");
        if (filter.From > filter.To)
            ModelState.AddModelError(nameof(filter.From), "開始日期不得晚於結束日期。");
        if (!ModelState.IsValid) return View(filter);

        var query = db.WorkLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x => x.Title.Contains(term) ||
                (x.Project != null && x.Project.Contains(term)) || x.Content.Contains(term));
        }
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status);
        if (filter.From.HasValue) query = query.Where(x => x.WorkDate >= filter.From);
        if (filter.To.HasValue) query = query.Where(x => x.WorkDate <= filter.To);

        var month = filter.CalendarMonth ?? DateOnly.FromDateTime(DateTime.Today);
        month = new DateOnly(Math.Clamp(month.Year, 2000, 2100), month.Month, 1);
        filter.CalendarMonth = month;
        // Calendar follows the filters but is independent of table pagination.
        filter.CalendarItems = await query.Where(x => x.WorkDate >= month && x.WorkDate < month.AddMonths(1))
            .OrderBy(x => x.WorkDate).ThenBy(x => x.StartTime).ThenBy(x => x.Id).ToListAsync();
        filter.TotalCount = await query.CountAsync();
        filter.TotalHours = await query.SumAsync(x => (decimal?)x.Hours) ?? 0;
        filter.CompletedCount = await query.CountAsync(x => x.Status == WorkStatus.Completed);
        filter.ActiveCount = await query.CountAsync(x => x.Status == WorkStatus.InProgress);
        filter.Page = Math.Clamp(filter.Page, 1, filter.TotalPages);
        filter.Items = await query.OrderByDescending(x => x.WorkDate).ThenByDescending(x => x.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync();
        return View(filter);
    }

    [HttpGet]
    public IActionResult Create() => View("Edit", new WorkLog());

    [HttpPost]
    public async Task<IActionResult> Create(
        [Bind("WorkDate,Title,Project,Hours,Status,Content,NextSteps,StartTime,EndTime,Color")] WorkLog input)
    {
        if (!ModelState.IsValid) return View("Edit", input);
        Normalize(input);
        db.WorkLogs.Add(input);
        await db.SaveChangesAsync();
        TempData["Success"] = "工作日誌已新增。";
        return RedirectToAction(nameof(Details), new { id = input.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id)
    {
        var item = await db.WorkLogs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : View(item);
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id)
    {
        var item = await db.WorkLogs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : View(item);
    }

    [HttpPost]
    [ActionName("Edit")]
    public async Task<IActionResult> EditPost([FromRoute] int id)
    {
        var item = await db.WorkLogs.FindAsync(id);
        if (item is null) return NotFound();
        if (!await TryUpdateModelAsync(item, "", x => x.WorkDate, x => x.Title,
                x => x.Project, x => x.Hours, x => x.Status, x => x.Content, x => x.NextSteps,
                x => x.StartTime, x => x.EndTime, x => x.Color))
            return View("Edit", item);
        Normalize(item);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        TempData["Success"] = "變更已儲存。";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var item = await db.WorkLogs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : View(item);
    }

    [HttpPost]
    [ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] int id)
    {
        var item = await db.WorkLogs.FindAsync(id);
        if (item is null) return NotFound();
        db.WorkLogs.Remove(item);
        await db.SaveChangesAsync();
        TempData["Success"] = "工作日誌已刪除。";
        return RedirectToAction(nameof(Index));
    }

    private static void Normalize(WorkLog item)
    {
        item.Title = item.Title.Trim();
        item.Content = item.Content.Trim();
        item.Project = string.IsNullOrWhiteSpace(item.Project) ? null : item.Project.Trim();
        item.NextSteps = string.IsNullOrWhiteSpace(item.NextSteps) ? null : item.NextSteps.Trim();
        item.Hours = decimal.Round(item.Hours, 2);
    }
}


