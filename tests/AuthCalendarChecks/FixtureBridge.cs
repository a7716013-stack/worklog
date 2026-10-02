namespace AuthCalendarChecks;

// Test-only loopback bridge to the real application TestServer, with a synthetic signed-in user.
// Compiled into the test executable only; never part of WorkJournal.Web or its deployment.
public static class FixtureBridge
{
    public static async Task RunAsync(HttpClient signedIn)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://localhost:5188");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Run(async context =>
        {
            context.Response.Headers["X-AuthCalendar-Fixture"] = "isolated-sql";
            if (context.Request.Path == "/__fixture/stop" && context.Request.Method == "POST" && context.Request.Headers["X-Fixture-Control"] == "stop")
            { app.Lifetime.StopApplication(); return; }
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
            if (context.Request.ContentLength > 0 || context.Request.ContentType is not null)
                request.Content = new StreamContent(context.Request.Body);
            foreach (var header in context.Request.Headers)
                if (!new[] { "Host", "Cookie", "Connection", "Transfer-Encoding", "Content-Length" }.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                    if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && request.Content is not null)
                        request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            using var response = await signedIn.SendAsync(request, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (!new[] { "Transfer-Encoding", "Set-Cookie" }.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                    context.Response.Headers[header.Key] = header.Value.ToArray();
            if (context.Response.Headers.Location.ToString().StartsWith("https://localhost:7180"))
                context.Response.Headers.Location = context.Response.Headers.Location.ToString().Replace("https://localhost:7180", "http://localhost:5188");
            if (response.Headers.Location is { IsAbsoluteUri: true, Host: "accounts.google.com" } oauth)
            {
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(oauth.Query);
                var callback = new Uri(query["redirect_uri"]!);
                var code = query["scope"].ToString().Contains("calendar") ? "calendar-alice" : "alice";
                context.Response.Headers.Location = callback.PathAndQuery + "?code=" + code + "&state=" + Uri.EscapeDataString(query["state"]!);
            }
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
        Console.WriteLine("Authenticated test bridge ready: http://localhost:5188 (synthetic user, isolated SQL).");
        await app.RunAsync();
    }
}
