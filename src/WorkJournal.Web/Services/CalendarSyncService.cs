using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Services;

public record SyncValue(string Title, string Content, DateOnly Date, TimeOnly? Start, TimeOnly? End)
{
    public static SyncValue Local(WorkLog log) => new(log.Title, log.Content, log.WorkDate, log.StartTime, log.EndTime);
    public string Snapshot => JsonSerializer.Serialize(this, typeof(SyncValue), SnapshotOptions);
    private static readonly JsonSerializerOptions SnapshotOptions = new() { IgnoreReadOnlyProperties = true };
    public object Payload() => new CalendarEventInput
    {
        Title = Title, Description = Content, Start = Date.ToDateTime(Start ?? TimeOnly.MinValue),
        End = End.HasValue ? Date.ToDateTime(End.Value) : Date.AddDays(1).ToDateTime(TimeOnly.MinValue), IsAllDay = Start is null
    }.ToGooglePayload();
    public void Apply(WorkLog log)
    { log.Title = Title; log.Content = Content; log.WorkDate = Date; log.StartTime = Start; log.EndTime = End; log.UpdatedAt = DateTimeOffset.UtcNow; }
    public static SyncValue? Remote(JsonElement json)
    {
        // Work logs describe one day. Never silently truncate a recurring/multi-day event or its text.
        if (json.TryGetProperty("recurrence", out _) || json.TryGetProperty("recurringEventId", out _) ||
            (json.TryGetProperty("eventType", out var type) && type.GetString() != "default") ||
            (json.TryGetProperty("organizer", out var organizer) && (!organizer.TryGetProperty("self", out var self) || self.ValueKind != JsonValueKind.True))) return null;
        try
        {
            var e = CalendarEventViewModel.FromGoogle(json);
            if (string.IsNullOrWhiteSpace(e.Title) || e.Title.Length > 120 || (e.Description?.Length ?? 0) > 4000 || e.Start.Year < 2000 || e.Start.Year > 2100 || e.End <= e.Start) return null;
            var date = DateOnly.FromDateTime(e.Start.DateTime);
            if (e.IsAllDay ? e.End.Date != e.Start.Date.AddDays(1) : e.End.Date != e.Start.Date) return null;
            return new(e.Title.Trim(), string.IsNullOrWhiteSpace(e.Description) ? "（Google 行事曆事件）" : e.Description.Trim(), date,
                e.IsAllDay ? null : TimeOnly.FromDateTime(e.Start.DateTime), e.IsAllDay ? null : TimeOnly.FromDateTime(e.End.DateTime));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or FormatException or InvalidOperationException) { return null; }
    }
}

public class CalendarSyncService(JournalDbContext db, IGoogleCalendarService google, ICalendarSyncGateway remote)
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();
    public async Task<string> RunAsync(string userId, DateOnly month, int? resolveId = null, string? choice = null, CancellationToken ct = default)
    {
        if (month.Year is < 2000 or > 2100) throw new CalendarServiceException("請選擇 2000 至 2100 年的月份。");
        month = new(month.Year, month.Month, 1);
        var gate = Gates[(int)((uint)StringComparer.Ordinal.GetHashCode(userId) % (uint)Gates.Length)];
        await gate.WaitAsync(ct);
        int completed = 0, skipped = 0, problems = 0;
        try
        {
            if (!(await google.GetConnectionAsync(userId, ct)).IsConnected) throw new CalendarServiceException(CalendarServiceException.Reconnect);
            var links = await db.CalendarSyncLinks.Include(x => x.WorkLog).Where(x => x.ApplicationUserId == userId && !x.Retired).ToListAsync(ct);
            if (resolveId.HasValue && (!links.Any(x => x.Id == resolveId) || choice is not ("local" or "google")))
                throw new CalendarServiceException("找不到你的同步衝突，請重新整理。", 404);
            if (!resolveId.HasValue)
            {
                // Finish fetching all pages before creating local records; no absence-based deletions.
                var events = await google.GetEventsAsync(userId, new(month.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)),
                    new(month.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)), ct);
                var known = await db.CalendarSyncLinks.Where(x => x.ApplicationUserId == userId).Select(x => x.GoogleEventId).ToListAsync(ct);
                foreach (var e in events.Where(x => !known.Contains(x.Id)))
                {
                    var json = await remote.ReadAsync(userId, e.Id, ct);
                    if (json is null || IsDeleted(json.Value)) continue;
                    var value = SyncValue.Remote(json.Value);
                    if (value is null) { skipped++; continue; }
                    var log = new WorkLog { ApplicationUserId = userId };
                    value.Apply(log);
                    var link = new CalendarSyncLink { ApplicationUserId = userId, WorkLog = log, GoogleEventId = e.Id, Baseline = value.Snapshot, SyncedAt = DateTimeOffset.UtcNow };
                    db.CalendarSyncLinks.Add(link); await db.SaveChangesAsync(ct); links.Add(link); known.Add(e.Id); completed++;
                }
                var locals = await db.WorkLogs.Where(x => x.ApplicationUserId == userId && x.WorkDate >= month && x.WorkDate < month.AddMonths(1) &&
                    !db.CalendarSyncLinks.Any(l => l.WorkLogId == x.Id)).ToListAsync(ct);
                foreach (var log in locals)
                {
                    var link = new CalendarSyncLink { ApplicationUserId = userId, WorkLog = log, GoogleEventId = Guid.NewGuid().ToString("N") };
                    db.CalendarSyncLinks.Add(link); links.Add(link);
                }
                // Persist stable IDs before any remote insert: a timeout/retry cannot create duplicate events.
                await db.SaveChangesAsync(ct);
            }
            foreach (var link in links.Where(x => !resolveId.HasValue || x.Id == resolveId))
            {
                try { await Synchronize(link, resolveId == link.Id ? choice : null, ct); completed++; }
                catch (CalendarServiceException ex)
                {
                    link.Problem = ex.StatusCode == 412 ? "Google 事件剛被修改，請重新同步後選擇要保留的版本。" : ex.Message;
                    if (link.Problem.Length > 400) link.Problem = link.Problem[..400];
                    await db.SaveChangesAsync(ct); problems++;
                }
            }
            return $"同步完成：處理 {completed} 筆；待處理 {problems} 筆；略過 {skipped} 筆不支援的 Google 事件。";
        }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw new CalendarServiceException("同步期間資料已變更，已保留較新紀錄，請再同步一次。"); }
        finally { gate.Release(); }
    }
    private static bool IsDeleted(JsonElement json) => json.TryGetProperty("status", out var s) && s.GetString() == "cancelled";
    private async Task Synchronize(CalendarSyncLink link, string? choice, CancellationToken ct)
    {
        var user = link.ApplicationUserId;
        var json = await remote.ReadAsync(user, link.GoogleEventId, ct);
        bool deleted = json is null || IsDeleted(json.Value);
        var local = link.WorkLog is null ? null : SyncValue.Local(link.WorkLog);
        if (link.Baseline is null)
        {
            if (local is null) { link.Retired = true; await db.SaveChangesAsync(ct); return; }
            if (deleted)
            {
                // A cancelled ID cannot be reused after deletion on Google.
                if (json is not null) throw new CalendarServiceException("待建立的 Google 事件已被刪除，請保留本機紀錄並重新建立同步對應。");
                try { json = await remote.CreateAsync(user, link.GoogleEventId, local.Payload(), ct); }
                catch (CalendarServiceException ex) when (ex.StatusCode == 409) { json = await remote.ReadAsync(user, link.GoogleEventId, ct); }
            }
            if (json is null || !json.Value.TryGetProperty("extendedProperties", out var props) || !props.TryGetProperty("private", out var p) ||
                !p.TryGetProperty("workjournalSync", out var marker) || marker.GetString() != link.GoogleEventId)
                throw new CalendarServiceException("無法確認 Google 事件對應，未覆寫任何事件。");
            var created = SyncValue.Remote(json.Value);
            if (created is null) throw new CalendarServiceException("Google 事件不符合單日日誌格式，已保留本機紀錄。");
            if (created.Snapshot != local.Snapshot)
            {
                if (choice is null) throw new CalendarServiceException("首次同步回應中斷後兩邊內容不同，請選擇保留本機或 Google 版本。");
                if (choice == "google") { created.Apply(link.WorkLog!); local = created; }
                else
                {
                    var etag = json.Value.GetProperty("etag").GetString();
                    if (string.IsNullOrWhiteSpace(etag)) throw new CalendarServiceException("Google 未提供版本資訊，已暫停更新。");
                    await remote.WriteAsync(user, link.GoogleEventId, etag, local.Payload(), ct);
                }
            }
            link.Baseline = local.Snapshot;
        }
        else
        {
            var value = deleted ? null : SyncValue.Remote(json!.Value);
            if (!deleted && value is null) throw new CalendarServiceException("Google 事件不符合單日日誌格式；已保留兩邊內容。請在 Google 改回單日事件再同步。");
            bool localChanged = local?.Snapshot != link.Baseline, remoteChanged = value?.Snapshot != link.Baseline;
            if (local?.Snapshot != value?.Snapshot && localChanged && remoteChanged && choice is null)
                throw new CalendarServiceException("兩邊均已修改（或刪除）。請選擇保留本機或 Google 版本。");
            bool push = choice == "local" || (choice is null && localChanged && !remoteChanged);
            if (push && local?.Snapshot != value?.Snapshot)
            {
                if (deleted && local is not null)
                {
                    // Start a fresh durable creation after explicitly choosing local over remote deletion.
                    link.GoogleEventId = Guid.NewGuid().ToString("N"); link.Baseline = null;
                    await db.SaveChangesAsync(ct); await Synchronize(link, null, ct); return;
                }
                var etag = json!.Value.GetProperty("etag").GetString();
                if (string.IsNullOrWhiteSpace(etag)) throw new CalendarServiceException("Google 未提供版本資訊，已暫停更新。");
                if (local is null) { await remote.RemoveAsync(user, link.GoogleEventId, etag, ct); link.Retired = true; }
                else { await remote.WriteAsync(user, link.GoogleEventId, etag, local.Payload(), ct); link.Baseline = local.Snapshot; }
            }
            else if (!push && local?.Snapshot != value?.Snapshot)
            {
                if (value is null)
                {
                    if (link.WorkLog is not null) db.WorkLogs.Remove(link.WorkLog);
                    link.WorkLog = null; link.WorkLogId = null; link.Retired = true;
                }
                else
                {
                    link.WorkLog ??= new WorkLog { ApplicationUserId = user };
                    value.Apply(link.WorkLog); link.Baseline = value.Snapshot;
                }
            }
            else { link.Baseline = local?.Snapshot; if (local is null) link.Retired = true; }
        }
        link.Problem = null; link.SyncedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
