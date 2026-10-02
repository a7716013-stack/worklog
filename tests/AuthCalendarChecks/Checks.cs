using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WorkJournal.Web.Data;
using WorkJournal.Web.Security;
using WorkJournal.Web.ViewModels;
namespace AuthCalendarChecks;

public static class Checks
{
    private static int passed;
    public static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException("FAIL: " + label); passed++; Console.WriteLine("PASS: " + label); }
    public static async Task Main(string[] args)
    {
        // Synthetic credentials are confined to this process. No personal Google secrets or remote APIs.
        Environment.SetEnvironmentVariable("Authentication__Google__ClientId", "fixture-client");
        Environment.SetEnvironmentVariable("Authentication__Google__ClientSecret", "fixture-secret");
        Environment.SetEnvironmentVariable("Authentication__Google__PublicOrigin", "https://localhost:7180");
        Environment.SetEnvironmentVariable("Authentication__TrustedProxyKey", "fixture-proxy-key");
        var database = "WorkJournalAuthChecks_" + Guid.NewGuid().ToString("N");
        var connection = $@"Server=.\SQLEXPRESS;Database={database};Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;";
        var options = new DbContextOptionsBuilder<JournalDbContext>().UseSqlServer(connection).Options;
        await using var setup = new JournalDbContext(options);
        try
        {
            await setup.GetService<IMigrator>().MigrateAsync("20260922212853_AddPaperTrading");
            await setup.Database.ExecuteSqlRawAsync("INSERT INTO WorkLogs (WorkDate,Title,Project,Hours,Status,Content,NextSteps,CreatedAt,UpdatedAt,StartTime,EndTime,Color) VALUES ('2026-10-01',N'legacy private',NULL,1,0,N'legacy data',NULL,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET(),NULL,NULL,0)");
            await setup.Database.ExecuteSqlRawAsync("INSERT INTO PaperTradingAccounts (Id,Name,InitialCash,Cash,RealizedProfitLoss,Generation,CreatedAt,UpdatedAt) VALUES (1,N'legacy paper',100000,100000,0,NEWID(),SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET())");
            await setup.Database.MigrateAsync();
            Check(await setup.WorkLogs.CountAsync(x => x.ApplicationUserId == null && x.Title == "legacy private") == 1, "Migration preserves unassigned legacy logs");
            Check(await setup.PaperTradingAccounts.CountAsync(x => x.Name == "legacy paper") == 1, "Migration preserves PaperTrading data");
            Check(!setup.Database.HasPendingModelChanges(), "Migration snapshot matches current model");
            var google = new FakeGoogle();
            await using var factory = new FixtureFactory(connection, google);
            using var anonymous = factory.Browser();
            Check((await anonymous.GetAsync("https://untrusted.example/signin-google?code=fixture")).StatusCode == HttpStatusCode.BadRequest, "Untrusted callback host rejected");
            using (var proxied = new HttpRequestMessage(HttpMethod.Get, "https://127.0.0.1/signin-google?code=fixture"))
            {
                proxied.Headers.Add("X-WorkJournal-Proxy-Key", "fixture-proxy-key");
                Check((await anonymous.SendAsync(proxied)).StatusCode == HttpStatusCode.Redirect, "Trusted proxy restores only configured public origin");
            }
            foreach (var path in new[] { "/WorkLogs", "/WorkLogs/Create", "/WorkLogs/Details/1", "/GoogleCalendar", "/GoogleCalendar/Events" })
            {
                using var response = await anonymous.GetAsync(path);
                Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.ToString().Contains("/Account/Login"), "Anonymous blocked: " + path);
            }
            Check((await anonymous.GetAsync("/StockAnalysis")).StatusCode == HttpStatusCode.OK, "Stock analysis remains public");
            Check((await anonymous.PostAsync("/Account/GoogleLogin", new FormUrlEncodedContent([]))).StatusCode == HttpStatusCode.BadRequest, "Login POST requires antiforgery");
            Check((await anonymous.GetAsync("/signin-google?code=alice")).Headers.Location?.ToString() == "/Account/Login?failed=true", "Login callback rejects missing state");
            Check((await anonymous.GetAsync("/Account/GoogleCallback?returnUrl=https://evil.test")).Headers.Location!.ToString().Contains("failed"), "No external cookie cannot complete login");
            google.Unverified = true;
            var unverified = await Post(anonymous, "/Account/GoogleLogin", [], "/Account/Login");
            var unverifiedState = QueryHelpers.ParseQuery(unverified.Headers.Location!.Query)["state"].ToString();
            Check((await anonymous.GetAsync("/signin-google?code=alice&state=" + Uri.EscapeDataString(unverifiedState))).Headers.Location!.ToString().Contains("failed"), "Unverified Google email rejected");
            google.Unverified = false;
            using var alice = factory.Browser(); using var bob = factory.Browser();
            await Login(alice, "alice", "https://evil.test"); await Login(bob, "bob", "/WorkLogs");
            string aliceId; string bobId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<JournalDbContext>();
                aliceId = (await db.Users.SingleAsync(x => x.Email == "alice@example.test")).Id;
                bobId = (await db.Users.SingleAsync(x => x.Email == "bob@example.test")).Id;
                Check(await db.UserLogins.CountAsync() == 2, "Google subjects linked to separate Identity accounts");
                Check(await db.UserTokens.CountAsync() == 0, "No OAuth tokens in Identity token table");
            }
            var fields = new Dictionary<string,string> { ["WorkDate"] = "2026-10-01", ["Title"] = "Alice secret", ["Content"] = "Private content", ["Hours"] = "1", ["Status"] = "0", ["Color"] = "1", ["ApplicationUserId"] = bobId };
            var created = await Post(alice, "/WorkLogs/Create", fields, "/WorkLogs/Create");
            Check(created.StatusCode == HttpStatusCode.Redirect, "Authenticated WorkLog create succeeds");
            var logId = int.Parse(created.Headers.Location!.ToString().Split('/').Last());
            using (var scope = factory.Services.CreateScope())
                Check((await scope.ServiceProvider.GetRequiredService<JournalDbContext>().WorkLogs.FindAsync(logId))!.ApplicationUserId == aliceId, "Browser cannot forge WorkLog owner");
            foreach (var action in new[] { "Details", "Edit", "Delete" })
                Check((await bob.GetAsync($"/WorkLogs/{action}/{logId}")).StatusCode == HttpStatusCode.NotFound, "User B cannot read A " + action);
            Check((await Post(bob, $"/WorkLogs/Edit/{logId}", fields, "/WorkLogs/Create")).StatusCode == HttpStatusCode.NotFound, "User B cannot edit A WorkLog");
            Check((await Post(bob, $"/WorkLogs/Delete/{logId}", [], "/WorkLogs/Create")).StatusCode == HttpStatusCode.NotFound, "User B cannot delete A WorkLog");
            Check((await alice.GetAsync("/WorkLogs/Details/1")).StatusCode == HttpStatusCode.NotFound, "Unassigned legacy log stays inaccessible");
            var bobIndex = await bob.GetStringAsync("/WorkLogs?CalendarMonth=2026-10-01");
            Check(!bobIndex.Contains("Alice secret") && !bobIndex.Contains("legacy private"), "List/calendar/statistics isolate owners");
            var challenge = await StartCalendar(alice);
            var parameters = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
            Check(parameters["scope"].ToString().Contains(GoogleAuthSettings.CalendarScope) && parameters["access_type"] == "offline" && parameters["prompt"] == "consent", "Separate Calendar consent requests owned events and offline refresh");
            Check(parameters["redirect_uri"] == "https://localhost:7180/GoogleCalendar/OAuthCallback", "Calendar callback URL pinned to configured origin");
            Check((await alice.GetAsync("/GoogleCalendar/OAuthCallback?code=calendar-alice&state=corrupt")).Headers.Location!.ToString().Contains("authorizationFailed"), "Calendar callback rejects tampered state");
            await CompleteCalendar(alice, parameters["state"].ToString(), "calendar-alice");
            await CompleteCalendar(alice, parameters["state"].ToString(), "calendar-alice", true);
            Check((await alice.GetStringAsync("/GoogleCalendar")).Contains("alice@example.test"), "Calendar callback stores current user's connection");
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<JournalDbContext>();
                var connected = await db.GoogleCalendarConnections.SingleAsync();
                Check(connected.ApplicationUserId == aliceId && connected.IsConnected, "Connection belongs to initiator");
                Check(!connected.EncryptedRefreshToken!.Contains("fixture-refresh") && !connected.EncryptedAccessToken!.Contains("fixture-access"), "Access/refresh tokens encrypted at rest");
                var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("WorkJournal.GoogleCalendar.Tokens.v1", bobId);
                var isolated = false; try { protector.Unprotect(connected.EncryptedRefreshToken); } catch (CryptographicException) { isolated = true; }
                Check(isolated, "Token encryption bound to owner");
            }
            var combined = await alice.GetStringAsync("/WorkLogs?CalendarMonth=2026-10-01");
            Check(combined.Contains("Alice secret") && combined.Contains("Google fixture") && combined.Contains("google-event"), "Existing calendar aggregates local and Google events");
            Check(!combined.Contains("fixture-access") && !combined.Contains("fixture-refresh"), "Tokens never rendered in HTML");
            Check(!(await bob.GetStringAsync("/GoogleCalendar/Events?month=2026-10-01")).Contains("Google fixture"), "User B cannot fetch A Google events");
            var before = google.Mutations;
            var eventFields = new Dictionary<string,string> { ["Title"] = "New event", ["Start"] = "2026-10-05T09:00", ["End"] = "2026-10-05T10:00", ["IsAllDay"] = "false" };
            await Post(bob, "/GoogleCalendar/Create", eventFields, "/GoogleCalendar");
            Check(google.Mutations == before, "Unconnected B cannot mutate A calendar");
            Check((await alice.PostAsync("/GoogleCalendar/Delete/event1", new FormUrlEncodedContent([]))).StatusCode == HttpStatusCode.BadRequest, "Google mutations require antiforgery");
            Check((await Post(alice, "/GoogleCalendar/Create", eventFields, "/GoogleCalendar")).StatusCode == HttpStatusCode.Redirect, "Google event create");
            Check(google.LastPayload!.Contains("+08:00") || google.LastPayload.Contains("\\u002B08:00"), "Explicit Taipei UTC offset in payload");
            Check((await Post(alice, "/GoogleCalendar/Edit/event1", eventFields, "/GoogleCalendar")).StatusCode == HttpStatusCode.Redirect, "Google event edit");
            Check((await Post(alice, "/GoogleCalendar/Delete/event1", [], "/GoogleCalendar")).StatusCode == HttpStatusCode.Redirect, "Google event delete");
            Check(google.LastUrl!.Contains("/calendars/primary/events/event1"), "Calendar ID is server controlled");
            await Expire(factory);
            var refreshBefore = google.RefreshCount;
            await alice.GetAsync("/GoogleCalendar/Events?month=2026-10-01");
            Check(google.RefreshCount == refreshBefore + 1 && google.LastToken!.StartsWith("refreshed-"), "Expired access token refreshed automatically");
            google.OnceUnauthorized = true; refreshBefore = google.RefreshCount;
            await alice.GetAsync("/GoogleCalendar/Events?month=2026-10-01");
            Check(google.RefreshCount == refreshBefore + 1, "Google 401 refreshes and retries once");
            foreach (var failure in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
            {
                google.ApiFailure = failure;
                var html = await alice.GetStringAsync("/WorkLogs?CalendarMonth=2026-10-01");
                Check(html.Contains("Alice secret") && html.Contains("alert-warning"), "Local calendar survives Google " + (int)failure);
            }
            google.ApiFailure = null; google.Timeout = true;
            Check((await alice.GetStringAsync("/WorkLogs?CalendarMonth=2026-10-01")).Contains("Alice secret"), "Local calendar survives Google timeout");
            google.Timeout = false; google.Paged = true;
            Check((await alice.GetStringAsync("/GoogleCalendar/Events?month=2026-10-01")).Contains("event2"), "Calendar pagination follows nextPageToken");
            google.Paged = false;
            using (var document = JsonDocument.Parse("{\"id\":\"allday\",\"summary\":\"Trip\",\"start\":{\"date\":\"2026-10-01\"},\"end\":{\"date\":\"2026-10-03\"}}"))
            {
                var item = CalendarEventViewModel.FromGoogle(document.RootElement);
                Check(item.IsAllDay && item.OccursOn(new(2026,10,1)) && item.OccursOn(new(2026,10,2)) && !item.OccursOn(new(2026,10,3)), "All-day spans use exclusive end date");
            }
            using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(FakeGoogle.Event())))
                Check(CalendarEventViewModel.FromGoogle(doc.RootElement).Start.Hour == 7, "UTC events mapped to Taipei across midnight");
            var mismatch = await StartCalendar(alice);
            await CompleteCalendar(alice, QueryHelpers.ParseQuery(mismatch.Headers.Location!.Query)["state"]!, "calendar-bob", true);
            using (var scope = factory.Services.CreateScope())
                Check((await scope.ServiceProvider.GetRequiredService<JournalDbContext>().GoogleCalendarConnections.SingleAsync()).GoogleAccountId == "google-alice", "Different Google identity cannot replace connection");
            Check((await Post(alice, "/GoogleCalendar/Disconnect", [], "/GoogleCalendar")).StatusCode == HttpStatusCode.Redirect && google.Revocations == 1, "Disconnect revokes grant");
            using (var scope = factory.Services.CreateScope())
                Check(!await scope.ServiceProvider.GetRequiredService<JournalDbContext>().GoogleCalendarConnections.AnyAsync(), "Disconnect deletes credentials");
            google.MissingRefresh = true;
            var missing = await StartCalendar(alice);
            await CompleteCalendar(alice, QueryHelpers.ParseQuery(missing.Headers.Location!.Query)["state"]!, "calendar-alice", true);
            google.MissingRefresh = false; google.MissingScope = true;
            var limited = await StartCalendar(alice);
            await CompleteCalendar(alice, QueryHelpers.ParseQuery(limited.Headers.Location!.Query)["state"]!, "calendar-alice", true);
            google.MissingScope = false;
            using (var scope = factory.Services.CreateScope())
                Check(!await scope.ServiceProvider.GetRequiredService<JournalDbContext>().GoogleCalendarConnections.AnyAsync(), "Missing refresh or scope cannot establish connection");
            var reconnect = await StartCalendar(alice);
            await CompleteCalendar(alice, QueryHelpers.ParseQuery(reconnect.Headers.Location!.Query)["state"]!, "calendar-alice");
            google.ApiFailure = HttpStatusCode.Unauthorized;
            await alice.GetAsync("/GoogleCalendar/Events?month=2026-10-01");
            using (var scope = factory.Services.CreateScope())
                Check(!(await scope.ServiceProvider.GetRequiredService<JournalDbContext>().GoogleCalendarConnections.SingleAsync()).IsConnected, "Persistent 401 requires reauthorization");
            google.ApiFailure = null;
            reconnect = await StartCalendar(alice);
            await CompleteCalendar(alice, QueryHelpers.ParseQuery(reconnect.Headers.Location!.Query)["state"]!, "calendar-alice");
            google.RefreshFails = true; await Expire(factory);
            Check((await alice.GetStringAsync("/WorkLogs?CalendarMonth=2026-10-01")).Contains("Alice secret"), "invalid_grant preserves local calendar");
            using (var scope = factory.Services.CreateScope())
            {
                var c = await scope.ServiceProvider.GetRequiredService<JournalDbContext>().GoogleCalendarConnections.SingleAsync();
                Check(!c.IsConnected && c.EncryptedRefreshToken is null, "Invalid refresh clears credentials");
            }
            google.RefreshFails = false;
            var outstanding = await StartCalendar(alice);
            Check((await Post(alice, "/Account/Logout", [], "/GoogleCalendar")).StatusCode == HttpStatusCode.Redirect && (await alice.GetAsync("/WorkLogs")).StatusCode == HttpStatusCode.Redirect, "Logout clears session");
            await CompleteCalendar(alice, QueryHelpers.ParseQuery(outstanding.Headers.Location!.Query)["state"]!, "calendar-alice", true);
            Console.WriteLine($"Completed {passed} checks using synthetic Google responses. Real OAuth has NOT been tested.");
            if (args.Contains("--serve"))
            {
                await Login(alice, "alice", "/WorkLogs");
                await Post(alice, "/GoogleCalendar/Disconnect", [], "/GoogleCalendar");
                await FixtureBridge.RunAsync(alice);
            }
        }
        finally
        {
            if (!database.StartsWith("WorkJournalAuthChecks_") || database.Length != "WorkJournalAuthChecks_".Length + 32) throw new InvalidOperationException("Invalid test database");
            await setup.Database.EnsureDeletedAsync(); Console.WriteLine("Removed only this run's isolated database.");
        }
    }
    private static async Task Expire(FixtureFactory factory)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<JournalDbContext>();
        var c = await db.GoogleCalendarConnections.SingleAsync(); c.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
    }
    private static async Task<string> Token(HttpClient client, string page)
    {
        var html = await client.GetStringAsync(page);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success) throw new InvalidOperationException("Missing antiforgery token on " + page);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string,string> values, string page)
    {
        var copy = new Dictionary<string,string>(values) { ["__RequestVerificationToken"] = await Token(client, page) };
        return await client.PostAsync(path, new FormUrlEncodedContent(copy));
    }
    private static async Task Login(HttpClient browser, string name, string returnUrl)
    {
        var challenge = await Post(browser, "/Account/GoogleLogin", new() { ["returnUrl"] = returnUrl }, "/Account/Login");
        Check(challenge.StatusCode == HttpStatusCode.Redirect && challenge.Headers.Location!.Host == "accounts.google.com", "Google login challenge: " + name);
        var parameters = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        Check(!parameters["scope"].ToString().Contains("calendar") && parameters["scope"].ToString().Contains("openid"), "Login only asks basic identity scopes");
        Check(parameters.ContainsKey("code_challenge") && parameters["code_challenge_method"] == "S256", "Login uses PKCE");
        var ticket = await browser.GetAsync("/signin-google?code=" + name + "&state=" + Uri.EscapeDataString(parameters["state"]!));
        Check(ticket.StatusCode == HttpStatusCode.Redirect && ticket.Headers.Location!.ToString().Contains("GoogleCallback"), "Google handler validates callback and creates external ticket");
        var result = await browser.GetAsync(ticket.Headers.Location);
        Check(result.StatusCode == HttpStatusCode.Redirect && result.Headers.Location!.ToString() == "/WorkLogs", "Login completes and prevents open redirect");
        var cookies = string.Join(";", result.Headers.GetValues("Set-Cookie"));
        Check(cookies.Contains("__Host-WorkJournal=") && cookies.Contains("secure", StringComparison.OrdinalIgnoreCase) && cookies.Contains("httponly", StringComparison.OrdinalIgnoreCase), "Website cookie secure/HttpOnly");
    }
    private static Task<HttpResponseMessage> StartCalendar(HttpClient browser) => Post(browser, "/GoogleCalendar/Connect", [], "/GoogleCalendar");
    private static async Task CompleteCalendar(HttpClient browser, string state, string code, bool fails = false)
    {
        var result = await browser.GetAsync("/GoogleCalendar/OAuthCallback?code=" + code + "&state=" + Uri.EscapeDataString(state));
        Check(result.StatusCode == HttpStatusCode.Redirect && result.Headers.Location!.ToString() == (fails ? "/GoogleCalendar?authorizationFailed=true" : "/GoogleCalendar"), fails ? "Invalid Calendar authorization rejected" : "Calendar authorization callback completes");
    }
}
