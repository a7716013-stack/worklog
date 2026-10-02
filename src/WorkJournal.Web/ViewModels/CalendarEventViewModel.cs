using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.ViewModels;

public class CalendarEventViewModel
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "Local";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public bool IsAllDay { get; set; }
    public int Color { get; set; }
    public string? GoogleEventId { get; set; }
    public string CalendarId { get; set; } = "primary";
    public bool OccursOn(DateOnly day) => Start.Date < day.AddDays(1).ToDateTime(TimeOnly.MinValue) &&
        End.DateTime > day.ToDateTime(TimeOnly.MinValue);
    public static CalendarEventViewModel FromLocal(WorkLog log) => new()
    {
        Id = log.Id.ToString(CultureInfo.InvariantCulture), Title = log.Title, Description = log.Content,
        Start = new(log.WorkDate.ToDateTime(log.StartTime ?? TimeOnly.MinValue), TimeSpan.FromHours(8)),
        End = log.EndTime.HasValue ? new(log.WorkDate.ToDateTime(log.EndTime.Value), TimeSpan.FromHours(8)) :
            new(log.WorkDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)),
        IsAllDay = !log.StartTime.HasValue, Color = (int)log.Color
    };
    public static CalendarEventViewModel FromGoogle(JsonElement item)
    {
        var start = item.GetProperty("start");
        var allDay = start.TryGetProperty("date", out _);
        return new()
        {
            Id = item.GetProperty("id").GetString()!, GoogleEventId = item.GetProperty("id").GetString(),
            Source = "Google", Title = item.TryGetProperty("summary", out var title) ? title.GetString() ?? "（無標題）" : "（無標題）",
            Description = item.TryGetProperty("description", out var description) ? description.GetString() : null,
            Start = ReadTime(start), End = ReadTime(item.GetProperty("end")), IsAllDay = allDay
        };
    }
    private static DateTimeOffset ReadTime(JsonElement part) => part.TryGetProperty("date", out var date)
        ? new(DateOnly.ParseExact(date.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8))
        : DateTimeOffset.Parse(part.GetProperty("dateTime").GetString()!, CultureInfo.InvariantCulture).ToOffset(TimeSpan.FromHours(8));
}

public class CalendarEventInput : IValidatableObject
{
    [Required(ErrorMessage = "請填寫標題。"), StringLength(200), Display(Name = "標題")]
    public string Title { get; set; } = "";
    [StringLength(4000), Display(Name = "說明")] public string? Description { get; set; }
    [Display(Name = "開始（台北時間）")] public DateTime Start { get; set; } = DateTime.Today.AddHours(9);
    [Display(Name = "結束（台北時間）")] public DateTime End { get; set; } = DateTime.Today.AddHours(10);
    [Display(Name = "全天事件")] public bool IsAllDay { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Start.Year < 2000 || End.Year > 2101 || End.Year < 2000 || Start.Year > 2100)
            yield return new("日期須介於 2000 至 2100 年。", [nameof(Start), nameof(End)]);
        if ((IsAllDay ? End.Date <= Start.Date : End <= Start))
            yield return new("結束必須晚於開始；全天事件的結束日期為不包含的次日。", [nameof(End)]);
    }
    public static CalendarEventInput FromEvent(CalendarEventViewModel item) => new()
    { Title = item.Title, Description = item.Description, Start = item.Start.DateTime, End = item.End.DateTime, IsAllDay = item.IsAllDay };
    public object ToGooglePayload()
    {
        object Time(DateTime value) => IsAllDay ? new { date = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) } :
            (object)new { dateTime = new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.FromHours(8)).ToString("o"), timeZone = "Asia/Taipei" };
        return new { summary = Title.Trim(), description = Description?.Trim() ?? "", start = Time(Start), end = Time(End) };
    }
}

public record CalendarConnectionViewModel(bool IsConnected, bool NeedsReauthorization, string? Email, bool IsConfigured);
public record CalendarAggregation(IReadOnlyList<CalendarEventViewModel> Events, string? Warning);
