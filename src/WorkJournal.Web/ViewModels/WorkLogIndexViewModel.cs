using WorkJournal.Web.Models;
namespace WorkJournal.Web.ViewModels;

public class WorkLogIndexViewModel
{
    public DateOnly? CalendarMonth { get; set; }
    public IReadOnlyList<WorkLog> CalendarItems { get; set; } = [];
    public string? Search { get; set; }
    public WorkStatus? Status { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize => 10;
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
    public int ActiveCount { get; set; }
    public decimal TotalHours { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public IReadOnlyList<WorkLog> Items { get; set; } = [];
}

