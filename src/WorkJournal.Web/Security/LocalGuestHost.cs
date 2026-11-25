using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Http;
using WorkJournal.Domain.Scoring;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
namespace WorkJournal.Web.Security;
public static class LocalGuestHost
{
    public static bool Enabled(IConfiguration config) => config.GetValue<bool>("LocalGuest:Validated");
    public static bool IsLoopback(HttpContext context) =>
        context.Connection.RemoteIpAddress is {} remote && IPAddress.IsLoopback(remote) &&
        context.Connection.LocalIpAddress is {} local && IPAddress.IsLoopback(local) &&
        context.Request.Host.Host is "localhost" or "127.0.0.1" or "[::1]" && !context.Request.PathBase.HasValue;
    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Configuration["LocalGuest:Validated"]="false";
        if (!builder.Configuration.GetValue<bool>("LocalGuest:Enabled")) return;
        var sql = new SqlConnectionStringBuilder(builder.Configuration.GetConnectionString("JournalDatabase"));
        if (!builder.Environment.IsDevelopment() || sql.InitialCatalog != "WorkJournal_LocalGuest_331" ||
            sql.DataSource is not (@".\SQLEXPRESS" or @"localhost\SQLEXPRESS") || !sql.IntegratedSecurity)
            throw new InvalidOperationException("Local guest requires Development and the isolated local WorkJournal_LocalGuest_331 database.");
        builder.Configuration["LocalGuest:Validated"]="true";
        builder.Configuration["MarketData:Enabled"]="false";
        builder.Configuration["StrategyCenter:Enabled"]="false";
        builder.Configuration["RadarTracking:SchedulerEnabled"]="false";
        builder.Configuration["InvestmentInsights:SchedulerEnabled"]="false";
    }
    public static void Register(WebApplicationBuilder builder)
    {
        if (!Enabled(builder.Configuration)) return;
        builder.Services.AddDataProtection().SetApplicationName("WorkJournal.LocalGuest.331");
        builder.Services.ConfigureApplicationCookie(o => { o.Cookie.Name="WorkJournal.LocalGuest.331"; o.Cookie.SecurePolicy=CookieSecurePolicy.SameAsRequest; });
        builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter, GuestNetworkFilter>();
        builder.Services.AddScoped<ITaiwanMarketRankingProvider, GuestRanking>();
        builder.Services.AddScoped<IRadarTrackingDataProvider, GuestTracking>();
    }
}
public sealed class LocalGuestBoundary(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IConfiguration config)
    {
        if (LocalGuestHost.Enabled(config))
        {
            if (!LocalGuestHost.IsLoopback(context)) { context.Response.StatusCode=404; return; }
            if (context.Request.Path.StartsWithSegments("/Account/GoogleLogin") || context.Request.Path.StartsWithSegments("/Account/GoogleCallback") || context.Request.Path.StartsWithSegments("/signin-google")) { context.Response.StatusCode=404; return; }
        }
        await next(context);
    }
}
public sealed class GuestNetworkFilter : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder => { next(builder); builder.PrimaryHandler=new DenyNetwork(); };
    private sealed class DenyNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new HttpRequestException("LOCAL_GUEST_EXTERNAL_NETWORK_DISABLED");
    }
}
public sealed class GuestRanking : ITaiwanMarketRankingProvider
{
    public Task<MarketSnapshot> GetAsync(CancellationToken ct)
    {
        var day=DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
        while(day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) day=day.AddDays(-1);
        return Task.FromResult(new MarketSnapshot(LocalGuestFixtures.Stocks.Select((s,i)=>new MarketQuote(s,day,100+i,1,1000000+i*10000)).ToArray(),[],["本機合成測試行情；非真實資料或研究績效。"],DateTimeOffset.UtcNow));
    }
}
public sealed class GuestTracking(FinMindStockService stocks) : IRadarTrackingDataProvider
{
    public Task<SwingBar[]> PricesAsync(string symbol,DateOnly from,DateOnly to,CancellationToken ct)=>stocks.TrackingPricesAsync(symbol,from,to,ct);
    public Task<RadarResearch> ResearchAsync(MarketRadarStock item,CancellationToken ct)=>Task.FromResult(new RadarResearch("合成測試",null,null,null,null,[]) { Warnings=["SYNTHETIC_FIXTURE_NOT_REAL_VALIDATION"] });
}
