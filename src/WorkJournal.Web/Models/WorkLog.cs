using System.ComponentModel.DataAnnotations;
namespace WorkJournal.Web.Models;

public class WorkLog
{
    public int Id { get; set; }

    [Display(Name = "日期"), DataType(DataType.Date)]
    [WorkDateRange]
    public DateOnly WorkDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Display(Name = "工作項目"), Required(ErrorMessage = "請填寫工作項目。")]
    [StringLength(120, ErrorMessage = "工作項目最多 120 字。")]
    public string Title { get; set; } = "";

    [Display(Name = "專案／客戶"), StringLength(80, ErrorMessage = "專案／客戶最多 80 字。")]
    public string? Project { get; set; }

    [Display(Name = "工時")]
    [Range(typeof(decimal), "0", "24", ErrorMessage = "單筆工時須介於 0 至 24 小時。")]
    public decimal Hours { get; set; }

    [Display(Name = "進度"), EnumDataType(typeof(WorkStatus), ErrorMessage = "請選擇有效的進度。")]
    public WorkStatus Status { get; set; } = WorkStatus.InProgress;

    [Display(Name = "工作內容"), Required(ErrorMessage = "請填寫工作內容。")]
    [StringLength(4000, ErrorMessage = "工作內容最多 4000 字。")]
    public string Content { get; set; } = "";

    [Display(Name = "後續待辦"), StringLength(2000, ErrorMessage = "後續待辦最多 2000 字。")]
    public string? NextSteps { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

