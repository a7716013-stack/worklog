using System.Text.Json;
using System.Text.Json.Nodes;
namespace WorkJournal.Web.Services;

public interface ICalendarSyncGateway
{
    Task<JsonElement?> ReadAsync(string userId, string eventId, CancellationToken ct);
    Task<JsonElement> CreateAsync(string userId, string eventId, object payload, CancellationToken ct);
    Task<JsonElement> WriteAsync(string userId, string eventId, string etag, object payload, CancellationToken ct);
    Task RemoveAsync(string userId, string eventId, string etag, CancellationToken ct);
}
