using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using WorkJournal.Web.ViewModels;
namespace AuthCalendarChecks;

public static class SyncChecks
{
    public static async Task Run(FixtureFactory factory, string alice, string bob)
    {
        var fake = new SyncRemote();
        async Task<string> Run(string user, int? id = null, string? choice = null)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JournalDbContext>();
            return await new CalendarSyncService(db, fake, fake).RunAsync(user, new(2035, 1, 1), id, choice);
        }
        async Task<T> Db<T>(Func<JournalDbContext,Task<T>> action)
        { using var scope = factory.Services.CreateScope(); return await action(scope.ServiceProvider.GetRequiredService<JournalDbContext>()); }
        int localId = await Db(async db => { var log = new WorkLog { ApplicationUserId = alice, WorkDate = new(2035,1,5), Title = "Outbound", Content = "Local content", Project = "Private project", Hours = 3 }; db.Add(log); await db.SaveChangesAsync(); return log.Id; });
        fake.Seed("incoming", "Incoming");
        fake.Seed("multiday", "Multi-day", multiDay:true);
        await Run(alice);
        var link = await Db(db => db.CalendarSyncLinks.AsNoTracking().SingleAsync(x => x.WorkLogId == localId));
        Checks.Check(fake.Events[link.GoogleEventId]["summary"]!.GetValue<string>() == "Outbound", "Sync exports local work log");
        Checks.Check(await Db(db => db.WorkLogs.AnyAsync(x => x.ApplicationUserId == alice && x.Title == "Incoming")), "Sync imports Google event");
        Checks.Check(!await Db(db => db.WorkLogs.AnyAsync(x => x.Title == "Multi-day")), "Sync does not truncate multi-day events");
        var count = fake.Events.Count;
        await Run(alice);
        Checks.Check(fake.Events.Count == count && await Db(db => db.CalendarSyncLinks.CountAsync()) == 2, "Repeated sync is idempotent");
        await Db(async db => { var l = await db.WorkLogs.SingleAsync(x => x.Id == localId); l.Title = "Local changed"; await db.SaveChangesAsync(); return 0; });
        await Run(alice);
        Checks.Check(fake.Events[link.GoogleEventId]["summary"]!.GetValue<string>() == "Local changed", "Local edits update mapped Google event");
        fake.Edit(link.GoogleEventId, "Google changed");
        await Run(alice);
        Checks.Check(await Db(db => db.WorkLogs.AnyAsync(x => x.Id == localId && x.Title == "Google changed" && x.Project == "Private project" && x.Hours == 3)), "Google edits preserve local-only fields");
        await Db(async db => { var l = await db.WorkLogs.SingleAsync(x => x.Id == localId); l.Title = "Local conflict"; await db.SaveChangesAsync(); return 0; });
        fake.Edit(link.GoogleEventId, "Remote conflict");
        await Run(alice);
        Checks.Check(await Db(db => db.CalendarSyncLinks.AnyAsync(x => x.Id == link.Id && x.Problem != null)) && fake.Events[link.GoogleEventId]["summary"]!.GetValue<string>() == "Remote conflict", "Concurrent edits preserve both versions");
        try { await Run(bob, link.Id, "local"); throw new Exception("Cross-user sync accepted"); }
        catch (CalendarServiceException ex) { Checks.Check(ex.StatusCode == 404, "Sync conflict resolution checks owner"); }
        await Run(alice, link.Id, "google");
        Checks.Check(await Db(db => db.WorkLogs.AnyAsync(x => x.Id == localId && x.Title == "Remote conflict")), "Explicit Google conflict resolution");
        fake.PreconditionFailure = true;
        await Db(async db => { var l = await db.WorkLogs.SingleAsync(x => x.Id == localId); l.Title = "Retry change"; await db.SaveChangesAsync(); return 0; });
        await Run(alice);
        Checks.Check(await Db(db => db.CalendarSyncLinks.AnyAsync(x => x.Id == link.Id && x.Problem != null)), "ETag precondition failure is visible and retryable");
        fake.PreconditionFailure = false;
        await Run(alice);
        Checks.Check(fake.Events[link.GoogleEventId]["summary"]!.GetValue<string>() == "Retry change", "Conditional update retry succeeds");
        fake.Events.Remove("incoming");
        await Run(alice);
        Checks.Check(!await Db(db => db.WorkLogs.AnyAsync(x => x.ApplicationUserId == alice && x.Title == "Incoming")), "Remote deletion removes mapped local log");
        await Db(async db => { db.WorkLogs.Remove(await db.WorkLogs.SingleAsync(x => x.Id == localId)); await db.SaveChangesAsync(); return 0; });
        await Run(alice);
        Checks.Check(!fake.Events.ContainsKey(link.GoogleEventId), "Local deletion propagates using durable tombstone");
        int retryId = await Db(async db => { var l = new WorkLog { ApplicationUserId = alice, WorkDate = new(2035,1,6), Title = "Timeout retry", Content = "Body" }; db.Add(l); await db.SaveChangesAsync(); return l.Id; });
        fake.TimeoutAfterInsert = true;
        await Run(alice);
        var afterTimeout = fake.Events.Count;
        await Run(alice);
        Checks.Check(fake.Events.Count == afterTimeout && await Db(db => db.CalendarSyncLinks.AnyAsync(x => x.WorkLogId == retryId && x.Baseline != null && x.Problem == null)), "Lost insert response retries without duplicate event");
        fake.FailList = true;
        try { await Run(alice); } catch (CalendarServiceException) { }
        Checks.Check(await Db(db => db.WorkLogs.AnyAsync(x => x.Id == retryId)), "Google outage never infers deletion from missing listing");
        fake.FailList = false;
    }
}

public class SyncRemote : ICalendarSyncGateway, IGoogleCalendarService
{
    public Dictionary<string,JsonObject> Events { get; } = [];
    public bool PreconditionFailure, TimeoutAfterInsert, FailList;
    private int revision;
    public void Seed(string id, string title, bool multiDay = false)
    {
        Events[id] = JsonNode.Parse(JsonSerializer.Serialize(new { id, summary = title, description = "Remote body", etag = "\"1\"", start = new { date = "2035-01-03" }, end = new { date = multiDay ? "2035-01-05" : "2035-01-04" } }))!.AsObject();
    }
    public void Edit(string id, string title) { Events[id]["summary"] = title; Events[id]["etag"] = "\"" + (++revision) + "\""; }
    private static JsonElement Json(JsonObject node) => JsonSerializer.SerializeToElement(node);
    public Task<JsonElement?> ReadAsync(string u, string id, CancellationToken ct) => Task.FromResult(Events.TryGetValue(id, out var e) ? (JsonElement?)Json(e) : null);
    public Task<JsonElement> CreateAsync(string u, string id, object payload, CancellationToken ct)
    {
        if (Events.ContainsKey(id)) throw new CalendarServiceException("Conflict",409);
        var e = JsonSerializer.SerializeToNode(payload)!.AsObject(); e["id"] = id; e["etag"] = "\"" + (++revision) + "\"";
        e["extendedProperties"] = new JsonObject { ["private"] = new JsonObject { ["workjournalSync"] = id } };
        Events[id] = e;
        if (TimeoutAfterInsert) { TimeoutAfterInsert = false; throw new CalendarServiceException("Timeout"); }
        return Task.FromResult(Json(e));
    }
    public Task<JsonElement> WriteAsync(string u, string id, string etag, object payload, CancellationToken ct)
    {
        if (PreconditionFailure || Events[id]["etag"]!.GetValue<string>() != etag) throw new CalendarServiceException("Changed",412);
        foreach (var kv in JsonSerializer.SerializeToNode(payload)!.AsObject()) Events[id][kv.Key] = kv.Value?.DeepClone();
        Events[id]["etag"] = "\"" + (++revision) + "\"";
        return Task.FromResult(Json(Events[id]));
    }
    public Task RemoveAsync(string u, string id, string etag, CancellationToken ct) { if (Events[id]["etag"]!.GetValue<string>() != etag) throw new CalendarServiceException("Changed",412); Events.Remove(id); return Task.CompletedTask; }
    public Task<CalendarConnectionViewModel> GetConnectionAsync(string u, CancellationToken ct = default) => Task.FromResult(new CalendarConnectionViewModel(true,false,"fixture@example.test",true));
    public Task<IReadOnlyList<CalendarEventViewModel>> GetEventsAsync(string u, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    { if (FailList) throw new CalendarServiceException("Unavailable",503); return Task.FromResult<IReadOnlyList<CalendarEventViewModel>>(Events.Values.Select(x => CalendarEventViewModel.FromGoogle(Json(x))).ToList()); }
    public Task SaveAuthorizationAsync(string u,string a,string e,JsonElement t,CancellationToken ct=default) => throw new NotSupportedException();
    public Task<CalendarEventViewModel> GetEventAsync(string u,string e,CancellationToken ct=default) => throw new NotSupportedException();
    public Task CreateEventAsync(string u,CalendarEventInput i,CancellationToken ct=default) => throw new NotSupportedException();
    public Task UpdateEventAsync(string u,string e,CalendarEventInput i,CancellationToken ct=default) => throw new NotSupportedException();
    public Task DeleteEventAsync(string u,string e,CancellationToken ct=default) => throw new NotSupportedException();
    public Task RefreshAccessTokenAsync(string u,CancellationToken ct=default) => throw new NotSupportedException();
    public Task<bool> DisconnectAsync(string u,CancellationToken ct=default) => throw new NotSupportedException();
}
