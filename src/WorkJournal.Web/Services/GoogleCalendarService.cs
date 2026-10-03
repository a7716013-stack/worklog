using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Security;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Services;

public class GoogleCalendarService(JournalDbContext db, IHttpClientFactory clients,
    IDataProtectionProvider protection, IConfiguration configuration) : IGoogleCalendarService, ICalendarSyncGateway
{
    // Bounded locks serialize refresh/reconnect/disconnect within this process; SQL rowversion protects across instances.
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();
    private SemaphoreSlim Gate(string id) => Gates[(int)((uint)StringComparer.Ordinal.GetHashCode(id) % (uint)Gates.Length)];
    private IDataProtector Protector(string userId) => protection.CreateProtector("WorkJournal.GoogleCalendar.Tokens.v1", userId);
    private async Task<GoogleCalendarConnection> Connection(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var connection = await db.GoogleCalendarConnections.SingleOrDefaultAsync(x => x.ApplicationUserId == userId, ct);
        if (connection is null || !connection.IsConnected) throw new CalendarServiceException(CalendarServiceException.Reconnect);
        return connection;
    }
    public async Task<CalendarConnectionViewModel> GetConnectionAsync(string userId, CancellationToken ct = default)
    {
        var connection = await db.GoogleCalendarConnections.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationUserId == userId, ct);
        return new(connection?.IsConnected == true, connection is { IsConnected: false }, connection?.GoogleEmail, GoogleAuthSettings.IsConfigured(configuration));
    }
    public async Task SaveAuthorizationAsync(string userId, string accountId, string email, JsonElement tokens, CancellationToken ct = default)
    {
        // Never permit a callback to connect a different Google identity, even if email matches.
        if (!await db.UserLogins.AnyAsync(x => x.UserId == userId && x.LoginProvider == GoogleAuthSettings.LoginScheme && x.ProviderKey == accountId, ct))
            throw new CalendarServiceException("請使用目前登入網站的同一個 Google 帳號授權。");
        var scopes = Text(tokens, "scope") ?? "";
        if (!scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(GoogleAuthSettings.CalendarScope))
            throw new CalendarServiceException("尚未授予必要的行事曆事件權限，請重新連結並同意授權。");
        var gate = Gate(userId); await gate.WaitAsync(ct);
        try
        {
            var connection = await db.GoogleCalendarConnections.SingleOrDefaultAsync(x => x.ApplicationUserId == userId, ct);
            if (connection is null) { connection = new() { ApplicationUserId = userId }; db.Add(connection); }
            else await db.Entry(connection).ReloadAsync(ct);
            var refresh = Text(tokens, "refresh_token");
            if (string.IsNullOrWhiteSpace(refresh) && string.IsNullOrWhiteSpace(connection.EncryptedRefreshToken))
                throw new CalendarServiceException("Google 未提供離線授權，請重新連結並同意授權。");
            if (!string.IsNullOrWhiteSpace(refresh)) connection.EncryptedRefreshToken = Protector(userId).Protect(refresh);
            ApplyAccessToken(connection, tokens);
            connection.GoogleAccountId = accountId; connection.GoogleEmail = email;
            connection.CalendarId = "primary"; connection.GrantedScopes = scopes;
            connection.IsConnected = true; connection.UpdatedAt = DateTimeOffset.UtcNow;
            await Save(ct);
        }
        finally { gate.Release(); }
    }
    private static string? Text(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private void ApplyAccessToken(GoogleCalendarConnection connection, JsonElement json)
    {
        var token = Text(json, "access_token");
        if (string.IsNullOrWhiteSpace(token) || !json.TryGetProperty("expires_in", out var expires) || !expires.TryGetInt32(out var seconds) || seconds <= 0)
            throw new CalendarServiceException(CalendarServiceException.Unavailable);
        connection.EncryptedAccessToken = Protector(connection.ApplicationUserId).Protect(token);
        connection.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(seconds, 86400));
    }
    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new CalendarServiceException("行事曆連線狀態已變更，請重新整理頁面。"); }
    }
    private async Task Invalid(GoogleCalendarConnection connection, CancellationToken ct)
    {
        connection.IsConnected = false; connection.EncryptedAccessToken = null;
        connection.EncryptedRefreshToken = null; connection.AccessTokenExpiresAt = null;
        connection.UpdatedAt = DateTimeOffset.UtcNow; await Save(ct);
        throw new CalendarServiceException(CalendarServiceException.Reconnect, 401);
    }
    private async Task<string> AccessToken(string userId, bool force, CancellationToken ct)
    {
        var gate = Gate(userId); await gate.WaitAsync(ct);
        try
        {
            var connection = await Connection(userId, ct);
            await db.Entry(connection).ReloadAsync(ct);
            if (!connection.IsConnected) throw new CalendarServiceException(CalendarServiceException.Reconnect);
            try
            {
                if (!force && connection.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1) && connection.EncryptedAccessToken is not null)
                    return Protector(userId).Unprotect(connection.EncryptedAccessToken);
                if (connection.EncryptedRefreshToken is null) await Invalid(connection, ct);
                var refresh = Protector(userId).Unprotect(connection.EncryptedRefreshToken!);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string,string>
                    {
                        ["client_id"] = configuration["Authentication:Google:ClientId"] ?? "",
                        ["client_secret"] = configuration["Authentication:Google:ClientSecret"] ?? "",
                        ["refresh_token"] = refresh, ["grant_type"] = "refresh_token"
                    })
                };
                using var response = await Send(request, ct);
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized) await Invalid(connection, ct);
                await CheckStatus(response, ct);
                using var json = await ReadJson(response, ct);
                if (Text(json.RootElement, "scope") is string scopes && !scopes.Split(' ').Contains(GoogleAuthSettings.CalendarScope)) await Invalid(connection, ct);
                ApplyAccessToken(connection, json.RootElement);
                if (Text(json.RootElement, "refresh_token") is string replacement)
                    connection.EncryptedRefreshToken = Protector(userId).Protect(replacement);
                connection.UpdatedAt = DateTimeOffset.UtcNow; await Save(ct);
                return Protector(userId).Unprotect(connection.EncryptedAccessToken!);
            }
            catch (CryptographicException) { await Invalid(connection, ct); throw; }
        }
        finally { gate.Release(); }
    }
    public async Task RefreshAccessTokenAsync(string userId, CancellationToken ct = default) => _ = await AccessToken(userId, true, ct);
    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await clients.CreateClient("GoogleCalendarApi").SendAsync(request, ct); }
        catch (HttpRequestException) { throw new CalendarServiceException(CalendarServiceException.Unavailable); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new CalendarServiceException(CalendarServiceException.Unavailable); }
    }
    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response, CancellationToken ct)
    {
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { throw new CalendarServiceException(CalendarServiceException.Unavailable); }
    }
    private static async Task CheckStatus(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var reasons = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using var json = await ReadJson(response, ct);
                if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in new[] { "errors", "details" })
                        if (error.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array)
                            foreach (var item in list.EnumerateArray())
                                if (item.ValueKind == JsonValueKind.Object && Text(item, "reason") is string reason) reasons.Add(reason);
                }
            }
            catch (CalendarServiceException) { /* Preserve safe fallback for non-JSON provider failures. */ }
            var message = reasons.Overlaps(["accessNotConfigured", "SERVICE_DISABLED"])
                ? "Google Calendar API 尚未啟用或設定尚未生效（403 SERVICE_DISABLED）。請在 OAuth 用戶端所屬專案啟用 Calendar API，等待幾分鐘後重試。"
                : reasons.Overlaps(["insufficientPermissions", "ACCESS_TOKEN_SCOPE_INSUFFICIENT"])
                ? "Google 行事曆授權不足（403 insufficientPermissions）。請重新授權並勾選行事曆事件權限。"
                : reasons.Overlaps(["rateLimitExceeded", "userRateLimitExceeded", "quotaExceeded", "dailyLimitExceeded", "RATE_LIMIT_EXCEEDED"])
                ? "Google 行事曆已達使用額度或頻率限制（403 quota/rate limit）。請稍後重試；持續發生時請檢查專案配額。"
                : reasons.Contains("forbiddenForNonOrganizer")
                ? "這個事件只能由主辦人修改（403 forbiddenForNonOrganizer）。請改由事件主辦人操作。"
                : reasons.Contains("domainPolicy")
                ? "Google Workspace 管理政策禁止存取行事曆（403 domainPolicy）。請聯絡組織管理員。"
                : "Google 拒絕存取行事曆（403 forbidden）。請確認該帳號可開啟 Google 主要行事曆，並重新授權；若為公司帳號請確認管理政策。";
            // Only fixed messages leave the service; never render provider bodies, tokens or account data.
            throw new CalendarServiceException(message, 403);
        }
        throw new CalendarServiceException(response.StatusCode switch
        {
            HttpStatusCode.NotFound or HttpStatusCode.Gone => "找不到這個 Google 行事曆事件，可能已被刪除。",
            HttpStatusCode.Forbidden => "Google 拒絕存取此行事曆，請確認授權、Calendar API 設定或使用額度。",
            HttpStatusCode.TooManyRequests => "Google 行事曆請求過於頻繁，請稍後再試。",
            HttpStatusCode.BadRequest => "Google 無法接受這個事件，請檢查日期與內容。",
            _ => CalendarServiceException.Unavailable
        }, (int)response.StatusCode);
    }
    private async Task<JsonDocument?> Api(string userId, HttpMethod method, string suffix, object? payload, CancellationToken ct, string? etag = null)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await AccessToken(userId, attempt == 1, ct);
            using var request = new HttpRequestMessage(method, "https://www.googleapis.com/calendar/v3/calendars/primary/events" + suffix);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
            if (payload is not null) request.Content = JsonContent.Create(payload);
            using var response = await Send(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (attempt == 0) continue;
                await Invalid(await Connection(userId, ct), ct);
            }
            await CheckStatus(response, ct);
            return method == HttpMethod.Delete ? null : await ReadJson(response, ct);
        }
        throw new CalendarServiceException(CalendarServiceException.Reconnect);
    }
    public async Task<IReadOnlyList<CalendarEventViewModel>> GetEventsAsync(string userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        if (end <= start || end - start > TimeSpan.FromDays(62)) throw new ArgumentOutOfRangeException(nameof(end));
        var events = new List<CalendarEventViewModel>();
        string? page = null;
        var visitedPages = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            if (!visitedPages.Add(page ?? "") || visitedPages.Count > 100)
                throw new CalendarServiceException(CalendarServiceException.Unavailable);
            var suffix = "?singleEvents=true&orderBy=startTime&maxResults=2500&timeZone=Asia%2FTaipei&timeMin=" + Uri.EscapeDataString(start.ToString("o", CultureInfo.InvariantCulture)) +
                "&timeMax=" + Uri.EscapeDataString(end.ToString("o", CultureInfo.InvariantCulture));
            if (page is not null) suffix += "&pageToken=" + Uri.EscapeDataString(page);
            using var json = await Api(userId, HttpMethod.Get, suffix, null, ct);
            try
            {
                if (json!.RootElement.TryGetProperty("items", out var items))
                    foreach (var item in items.EnumerateArray())
                        if (Text(item, "status") != "cancelled") events.Add(CalendarEventViewModel.FromGoogle(item));
                page = Text(json.RootElement, "nextPageToken");
            }
            catch (Exception ex) when (ex is KeyNotFoundException or FormatException or InvalidOperationException)
            { throw new CalendarServiceException(CalendarServiceException.Unavailable); }
            if (events.Count > 25000) throw new CalendarServiceException("這個月份事件數量過多，暫時無法完整載入 Google 行事曆。");
        } while (page is not null);
        return events;
    }
    private static string EventPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 1024 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
            throw new CalendarServiceException("Google 事件識別碼無效。", 400);
        return "/" + Uri.EscapeDataString(id);
    }
    public async Task<CalendarEventViewModel> GetEventAsync(string userId, string eventId, CancellationToken ct = default)
    {
        using var json = await Api(userId, HttpMethod.Get, EventPath(eventId), null, ct);
        try { return CalendarEventViewModel.FromGoogle(json!.RootElement); }
        catch (Exception ex) when (ex is KeyNotFoundException or FormatException or InvalidOperationException)
        { throw new CalendarServiceException(CalendarServiceException.Unavailable); }
    }
    private static void Validate(CalendarEventInput input) => System.ComponentModel.DataAnnotations.Validator.ValidateObject(input, new(input), true);
    public async Task<JsonElement?> ReadAsync(string userId, string eventId, CancellationToken ct)
    {
        try { using var json = await Api(userId, HttpMethod.Get, EventPath(eventId), null, ct); return json!.RootElement.Clone(); }
        catch (CalendarServiceException ex) when (ex.StatusCode is 404 or 410) { return null; }
    }
    public async Task<JsonElement> CreateAsync(string userId, string eventId, object payload, CancellationToken ct)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(payload))!.AsObject();
        node["id"] = eventId;
        node["extendedProperties"] = new System.Text.Json.Nodes.JsonObject { ["private"] = new System.Text.Json.Nodes.JsonObject { ["workjournalSync"] = eventId } };
        using var json = await Api(userId, HttpMethod.Post, "", node, ct);
        return json!.RootElement.Clone();
    }
    public async Task<JsonElement> WriteAsync(string userId, string eventId, string etag, object payload, CancellationToken ct)
    {
        using var json = await Api(userId, HttpMethod.Patch, EventPath(eventId), payload, ct, etag);
        return json!.RootElement.Clone();
    }
    public async Task RemoveAsync(string userId, string eventId, string etag, CancellationToken ct)
    { using var json = await Api(userId, HttpMethod.Delete, EventPath(eventId), null, ct, etag); }
    public async Task CreateEventAsync(string userId, CalendarEventInput input, CancellationToken ct = default)
    { Validate(input); using var json = await Api(userId, HttpMethod.Post, "", input.ToGooglePayload(), ct); }
    public async Task UpdateEventAsync(string userId, string eventId, CalendarEventInput input, CancellationToken ct = default)
    { Validate(input); using var json = await Api(userId, HttpMethod.Patch, EventPath(eventId), input.ToGooglePayload(), ct); }
    public async Task DeleteEventAsync(string userId, string eventId, CancellationToken ct = default)
    { using var json = await Api(userId, HttpMethod.Delete, EventPath(eventId), null, ct); }
    public async Task<bool> DisconnectAsync(string userId, CancellationToken ct = default)
    {
        var gate = Gate(userId); await gate.WaitAsync(ct);
        try
        {
            var connection = await db.GoogleCalendarConnections.SingleOrDefaultAsync(x => x.ApplicationUserId == userId, ct);
            if (connection is null) return true;
            await db.Entry(connection).ReloadAsync(ct);
            string? token = null;
            try { if (connection.EncryptedRefreshToken is not null) token = Protector(userId).Unprotect(connection.EncryptedRefreshToken); }
            catch (CryptographicException) { /* Local credentials must still be removed if keys were lost. */ }
            db.Remove(connection); await Save(ct);
            if (token is null) return true;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/revoke")
                { Content = new FormUrlEncodedContent(new Dictionary<string,string> { ["token"] = token }) };
                using var response = await Send(request, ct);
                return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest;
            }
            catch (CalendarServiceException) { return false; }
        }
        finally { gate.Release(); }
    }
}
