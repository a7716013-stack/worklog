using System.Text.Json;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Services;

public interface IGoogleCalendarService
{
    Task<CalendarConnectionViewModel> GetConnectionAsync(string userId, CancellationToken ct = default);
    Task SaveAuthorizationAsync(string userId, string accountId, string email, JsonElement tokens, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEventViewModel>> GetEventsAsync(string userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
    Task<CalendarEventViewModel> GetEventAsync(string userId, string eventId, CancellationToken ct = default);
    Task CreateEventAsync(string userId, CalendarEventInput input, CancellationToken ct = default);
    Task UpdateEventAsync(string userId, string eventId, CalendarEventInput input, CancellationToken ct = default);
    Task DeleteEventAsync(string userId, string eventId, CancellationToken ct = default);
    Task RefreshAccessTokenAsync(string userId, CancellationToken ct = default);
    Task<bool> DisconnectAsync(string userId, CancellationToken ct = default);
}

public class CalendarServiceException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
    public const string Reconnect = "Google 行事曆授權已失效，請重新連結。";
    public const string Unavailable = "Google Calendar 暫時無法連線，請稍後再試。本地工作日誌仍可正常使用。";
}
