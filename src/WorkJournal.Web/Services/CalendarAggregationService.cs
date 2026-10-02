using WorkJournal.Web.Models;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Services;

public class CalendarAggregationService(IGoogleCalendarService google)
{
    public async Task<CalendarAggregation> GetAsync(string userId, DateOnly month, IReadOnlyList<WorkLog> logs, CancellationToken ct = default)
    {
        var events = logs.Select(CalendarEventViewModel.FromLocal).ToList();
        try
        {
            var connection = await google.GetConnectionAsync(userId, ct);
            if (connection.NeedsReauthorization) return new(events, CalendarServiceException.Reconnect);
            if (connection.IsConnected)
                events.AddRange(await google.GetEventsAsync(userId,
                    new(month.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)),
                    new(month.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)), ct));
            return new(events.OrderBy(x => x.Start).ToList(), null);
        }
        catch (CalendarServiceException ex) { return new(events, ex.Message); }
    }
}
