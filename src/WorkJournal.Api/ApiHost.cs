using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Web;
using WorkJournal.Web.Security;
using WorkJournal.Api.Services;

namespace WorkJournal.Api;
public static class ApiHost
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? customize = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args=args,ApplicationName=typeof(ApiHost).Assembly.GetName().Name });
        builder.Configuration["Api:Enabled"]="true";
        JournalHosting.AddServices(builder);
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(JournalHosting).Assembly)
            .AddApplicationPart(typeof(ApiHost).Assembly);
        builder.Services.AddScoped<IApiReadiness, ApiReadiness>();
        builder.Services.AddScoped<IDatabaseReadiness, DatabaseReadiness>();
        builder.Services.AddScoped<IMarketIndustries, MarketIndustries>();
        builder.Services.AddSingleton<ResearchReadService>();
        builder.Services.AddSingleton(new WorkJournal.Application.Research.EvidenceValidationReadService(Path.Combine(AppContext.BaseDirectory,"Research","validation-state.json")));
        builder.Services.AddSingleton<ValidationReadAdapter>();
        builder.Services.AddSingleton<FormulaResearchService>();
        builder.Services.AddAuthorization(options => {
            foreach (var policy in new[] { "ApiAuthenticatedUser", "ApiResearchRead", "ApiPaperTrade", "ApiAuthorityRead" })
                options.AddPolicy(policy, p => p.RequireAuthenticatedUser().RequireClaim(ClaimTypes.NameIdentifier));
            options.AddPolicy("ApiAuthorityAdmin", p => p.RequireAuthenticatedUser().RequireRole("Administrator"));
        });
        builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => {
            var login = options.Events.OnRedirectToLogin;
            var denied = options.Events.OnRedirectToAccessDenied;
            options.Events.OnRedirectToLogin = c => IsApi(c.Request.Path) ? ApiErrors.Write(c.HttpContext,401) : login(c);
            options.Events.OnRedirectToAccessDenied = c => IsApi(c.Request.Path) ? ApiErrors.Write(c.HttpContext,403) : denied(c);
        });
        builder.Services.Configure<ApiBehaviorOptions>(o => {
            o.SuppressMapClientErrors=true;
            o.InvalidModelStateResponseFactory = c => new ObjectResult(ApiErrors.Problem(c.HttpContext,400)) { StatusCode=400, ContentTypes={"application/problem+json"} };
        });
        builder.Services.AddRateLimiter(o => {
            o.RejectionStatusCode=429;
            o.OnRejected=(c,ct)=>new ValueTask(ApiErrors.Write(c.HttpContext,429));
            foreach (var name in new[] { "ReadPolicy", "WritePolicy", "ExpensiveResearchPolicy" })
                o.AddPolicy(name, context => {
                    var count=builder.Configuration.GetValue<int?>($"Api:RateLimits:{name}:PermitLimit");
                    var seconds=builder.Configuration.GetValue<int?>($"Api:RateLimits:{name}:WindowSeconds");
                    var key=context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                    // No invented production quota. Missing configuration is explicitly UNRESOLVED.
                    return count is >0 && seconds is >0
                        ? RateLimitPartition.GetFixedWindowLimiter(key,_=>new FixedWindowRateLimiterOptions { PermitLimit=count.Value,Window=TimeSpan.FromSeconds(seconds.Value),QueueLimit=0 })
                        : RateLimitPartition.GetNoLimiter(key);
                });
        });
        customize?.Invoke(builder);
        var app=builder.Build();
        app.Use(async (context,next) => {
            if (!IsApi(context.Request.Path)) { await next(); return; }
            var started=Stopwatch.GetTimestamp();
            context.Response.Headers["X-Trace-Id"]=Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
            context.Response.Headers.CacheControl="no-store";
            try {
                await next();
                if(context.Response.StatusCode>=400 && !context.Response.HasStarted && context.Response.ContentType is null)
                    await ApiErrors.Write(context,context.Response.StatusCode);
            }
            catch(OperationCanceledException) when(context.RequestAborted.IsCancellationRequested) { context.Abort(); }
            catch(Exception exception) {
                if(context.Response.HasStarted) { context.Abort(); return; }
                var status=exception is ApiFault fault ? fault.Status : exception is UnauthorizedAccessException ? 401 : 500;
                // Never log exception bodies/provider payloads or secrets.
                app.Logger.LogWarning("API failure {ExceptionType} TraceId {TraceId}",exception.GetType().Name,context.TraceIdentifier);
                context.Response.Clear();
                context.Response.Headers["X-Trace-Id"]=Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
                await ApiErrors.Write(context,status,(exception as ApiFault)?.Code);
            }
            finally {
                var subject=context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var safeSubject=subject is null ? "anonymous" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)))[..12];
                app.Logger.LogInformation("API {TraceId} {Subject} {Route} {StatusCode} {ElapsedMs}",context.TraceIdentifier,safeSubject,
                    (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched",context.Response.StatusCode,Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        });
        app.UseMiddleware<TrustedOriginMiddleware>();
        if(!app.Environment.IsDevelopment()) {
            app.UseWhen(c=>!IsApi(c.Request.Path), branch=>branch.UseExceptionHandler("/Home/Error"));
            app.UseHsts(); app.UseHttpsRedirection();
        }
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
        app.MapControllers();
        if(app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Api:OpenApi:Enabled"))
            app.MapGet("/openapi/v1.json",()=>Results.Json(OpenApi.ApiDocument.Create()));
        if(builder.Configuration.GetValue<bool?>("Api:HostWebUi") ?? true) {
            app.MapStaticAssets();
            app.MapControllerRoute("default","{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
        }
        return app;
    }
    public static bool IsApi(PathString path)=>path.StartsWithSegments("/api");
}

public sealed class ApiFault(int status,string code) : Exception(code)
{ public int Status {get;}=status; public string Code {get;}=code; }
public static class ApiErrors
{
    public static readonly string[] Codes=["VALIDATION_ERROR","AUTHENTICATION_REQUIRED","FORBIDDEN","RESOURCE_NOT_FOUND","CONFLICT","IDEMPOTENCY_CONFLICT","RATE_LIMITED","UPSTREAM_UNAVAILABLE","DATA_NOT_READY","BLOCKED_BY_AUTHORITY","BLOCKED_BY_DATA","STALE_VERSION","INTERNAL_ERROR"];
    public static ProblemDetails Problem(HttpContext c,int status,string? code=null) {
        code ??=status switch {400=>"VALIDATION_ERROR",401=>"AUTHENTICATION_REQUIRED",403=>"FORBIDDEN",404=>"RESOURCE_NOT_FOUND",409=>"CONFLICT",412=>"STALE_VERSION",422=>"BLOCKED_BY_DATA",429=>"RATE_LIMITED",503=>"UPSTREAM_UNAVAILABLE",_=>"INTERNAL_ERROR"};
        var p=new ProblemDetails {Status=status,Title=code,Type="urn:workjournal:problem:"+code.ToLowerInvariant(),Instance=c.Request.Path};
        p.Extensions["code"]=code;p.Extensions["traceId"]=Activity.Current?.TraceId.ToString() ?? c.TraceIdentifier;return p;
    }
    public static Task Write(HttpContext c,int status,string? code=null) {
        c.Response.StatusCode=status;
        return c.Response.WriteAsJsonAsync(Problem(c,status,code),options:null,contentType:"application/problem+json",cancellationToken:c.RequestAborted);
    }
}
