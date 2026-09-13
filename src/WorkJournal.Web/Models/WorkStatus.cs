using System.ComponentModel.DataAnnotations;
namespace WorkJournal.Web.Models;

public enum WorkStatus
{
    [Display(Name = "待開始")] Planned = 0,
    [Display(Name = "進行中")] InProgress = 1,
    [Display(Name = "已完成")] Completed = 2,
    [Display(Name = "待處理")] Blocked = 3
}
public static class WorkStatusExtensions
{
    public static string Label(this WorkStatus status) => status switch
    {
        WorkStatus.Planned => "待開始", WorkStatus.InProgress => "進行中",
        WorkStatus.Completed => "已完成", WorkStatus.Blocked => "待處理", _ => "未知"
    };
    public static string CssClass(this WorkStatus status) => status switch
    {
        WorkStatus.Completed => "done", WorkStatus.Blocked => "blocked",
        WorkStatus.InProgress => "active", _ => "planned"
    };
}

