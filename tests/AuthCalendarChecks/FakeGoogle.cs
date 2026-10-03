using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using WorkJournal.Web.Security;
namespace AuthCalendarChecks;

public class FakeGoogle : HttpMessageHandler
{
    public HttpStatusCode? ApiFailure { get; set; }
    public string? ApiFailureReason { get; set; }
    public bool Timeout { get; set; }
    public bool RefreshFails { get; set; }
    public bool MissingRefresh { get; set; }
    public bool MissingScope { get; set; }
    public bool OnceUnauthorized { get; set; }
    public bool Unverified { get; set; }
    public int RefreshCount { get; set; }
    public int Mutations { get; set; }
    public int Revocations { get; set; }
    public string? LastPayload { get; set; }
    public string? LastToken { get; set; }
    public string? LastUrl { get; set; }
    public string? LastIfMatch { get; set; }
    public bool Paged { get; set; }
    public static object Event(string id = "event1", string title = "Google fixture") => new
    {
        id, summary = title, description = "Fixture description", status = "confirmed",
        start = new { dateTime = "2026-10-01T23:00:00Z" }, end = new { dateTime = "2026-10-02T01:00:00Z" }
    };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        if (uri.AbsolutePath == "/token")
        {
            var values = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (values["grant_type"] == "refresh_token")
            {
                RefreshCount++;
                if (RefreshFails) return Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
                return Json(new { access_token = "refreshed-" + values["refresh_token"], expires_in = 3600, token_type = "Bearer", scope = GoogleAuthSettings.CalendarScope });
            }
            var code = values["code"].ToString();
            return Json(new Dictionary<string,object?> {
                ["access_token"] = "fixture-access-" + code, ["expires_in"] = 3600, ["token_type"] = "Bearer",
                ["refresh_token"] = code.StartsWith("calendar-") && !MissingRefresh ? "fixture-refresh-" + code : null,
                ["scope"] = code.StartsWith("calendar-") && !MissingScope ? GoogleAuthSettings.CalendarScope + " openid email profile" : "openid email profile"
            });
        }
        if (uri.AbsolutePath.EndsWith("/userinfo"))
        {
            if (uri.AbsolutePath != "/oauth2/v3/userinfo") throw new InvalidOperationException("Expected Google v3 userinfo endpoint.");
            var id = request.Headers.Authorization?.Parameter?.Contains("bob") == true ? "bob" : "alice";
            return Json(new { sub = "google-" + id, email = id + "@example.test", email_verified = !Unverified, name = "Fixture " + id, picture = "https://example.test/avatar.png" });
        }
        if (uri.AbsolutePath == "/revoke") { Revocations++; return Json(new { }); }
        if (uri.AbsolutePath.Contains("/calendar/v3/"))
        {
            LastToken = request.Headers.Authorization?.Parameter; LastUrl = uri.AbsoluteUri;
            LastIfMatch = request.Headers.TryGetValues("If-Match", out var match) ? match.Single() : null;
            if (Timeout) throw new TaskCanceledException("Fixture timeout");
            if (OnceUnauthorized) { OnceUnauthorized = false; return Json(new { error = "invalid_token" }, HttpStatusCode.Unauthorized); }
            if (ApiFailure is { } failure) return ApiFailureReason is null ? Json(new { error = "fixture" }, failure) : Json(new { error = new { errors = new[] { new { reason = ApiFailureReason } }, message = "PRIVATE_PROVIDER_PAYLOAD" } }, failure);
            if (request.Method != HttpMethod.Get)
            {
                Mutations++; LastPayload = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                return request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) : Json(Event());
            }
            if (uri.AbsolutePath.EndsWith("/events"))
            {
                if (Paged && !uri.Query.Contains("pageToken")) return Json(new { items = new[] { Event() }, nextPageToken = "page-two" });
                return Json(new { items = new[] { Event(Paged ? "event2" : "event1", Paged ? "Page two" : "Google fixture") } });
            }
            return Json(Event());
        }
        throw new InvalidOperationException("Unexpected fixture URL: " + uri.GetLeftPart(UriPartial.Path));
    }
    private static HttpResponseMessage Json(object data, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(JsonSerializer.Serialize(data), System.Text.Encoding.UTF8, "application/json") };
}
