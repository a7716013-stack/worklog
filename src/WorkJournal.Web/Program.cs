using WorkJournal.Web.Services.V2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    var messages = options.ModelBindingMessageProvider;
    messages.SetValueIsInvalidAccessor(value => $"輸入值「{value}」無效。");
    messages.SetAttemptedValueIsInvalidAccessor((value, field) => $"{field} 的值「{value}」無效。");
    messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"輸入值「{value}」無效。");
    messages.SetValueMustNotBeNullAccessor(field => "請填寫此欄位。");
    messages.SetValueMustBeANumberAccessor(field => $"{field} 必須為數字。");
});
builder.Services.AddDbContext<JournalDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("JournalDatabase"),
        sql => sql.EnableRetryOnFailure()));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddV2Foundations();
builder.Services.AddSingleton<WorkJournal.Web.Services.Evidence.IFullEvidenceScoreCalculator, WorkJournal.Web.Services.Evidence.FullEvidenceScoreCalculator>();
builder.Services.AddSingleton<WorkJournal.Web.Services.Evidence.V3CandidateCalculator>();
builder.Services.AddSingleton<WorkJournal.Web.Services.Research.ResearchEngine>();
builder.Services.AddSingleton<WorkJournal.Web.Services.HistoricalResearch.IHistoricalResearchDatasetBuilder, WorkJournal.Web.Services.HistoricalResearch.HistoricalDatasetBuilder>();
builder.Services.AddScoped<WorkJournal.Web.Services.Evidence.IFullEvidencePreparationService, WorkJournal.Web.Services.Evidence.FullEvidencePreparationService>();
builder.Services.AddHttpClient<WorkJournal.Web.Services.PaperIntradayService>(client => {
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 WorkJournal/1.5.1");
});
builder.Services.AddScoped<WorkJournal.Web.Services.HomeDashboardService>();
builder.Services.AddHttpClient<WorkJournal.Web.Services.InternationalIndicesService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 WorkJournal/1.4.2");
});
builder.Services.AddHttpClient<WorkJournal.Web.Services.TaiwanMarketRankingProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 WorkJournal/1.4.2");
});
builder.Services.AddScoped<WorkJournal.Web.Services.ITaiwanMarketRankingProvider>(sp => sp.GetRequiredService<WorkJournal.Web.Services.TaiwanMarketRankingProvider>());
builder.Services.AddScoped<WorkJournal.Web.Services.IMarketRadarHistoryProvider, WorkJournal.Web.Services.FinMindRadarHistoryProvider>();
builder.Services.AddScoped<WorkJournal.Web.Services.IMarketRadarService, WorkJournal.Web.Services.MarketRadarService>();
builder.Services.AddJournalAuthentication(builder.Configuration);
builder.Services.AddHttpClient<WorkJournal.Web.Services.FinMindStockService>(client =>
{
    client.BaseAddress = new Uri("https://api.finmindtrade.com/api/v4/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
// Opt in locally. The independently built market-data library has no dependency on MVC or EF.
if (builder.Configuration.GetValue<bool>("MarketData:Enabled"))
{
    var dataOptions = new WorkJournal.MarketData.MarketDataOptions();
    builder.Configuration.GetSection("MarketData").Bind(dataOptions);
    dataOptions.CacheDirectory = Path.GetFullPath(dataOptions.CacheDirectory,builder.Environment.ContentRootPath);
    builder.Services.AddSingleton(dataOptions);
    builder.Services.AddSingleton<WorkJournal.MarketData.SourceCache>();
    builder.Services.AddSingleton<WorkJournal.MarketData.RequestBudget>();
    builder.Services.AddHttpClient("MarketDataOfficial",client => { client.Timeout = TimeSpan.FromSeconds(20); client.DefaultRequestHeaders.UserAgent.ParseAdd("WorkJournal-Local/1.0"); });
    builder.Services.AddSingleton(sp => new WorkJournal.MarketData.MarketDataGateway(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("MarketDataOfficial"),sp.GetRequiredService<WorkJournal.MarketData.SourceCache>(),
        sp.GetRequiredService<WorkJournal.MarketData.RequestBudget>(),sp.GetRequiredService<TimeProvider>()));
}
builder.Services.AddHttpClient<WorkJournal.Web.Services.EtfOfficialService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 WorkJournal/1.4.2");
});
builder.Services.AddOptions<WorkJournal.Web.Models.PaperTradingOptions>()
    .Bind(builder.Configuration.GetSection("PaperTrading"))
    .Validate(x => x.IsValid(), "虛擬交易參數無效。")
    .ValidateOnStart();
builder.Services.AddScoped<WorkJournal.Web.Services.IPaperTradingService, WorkJournal.Web.Services.PaperTradingService>();
WorkJournal.Web.Services.RadarTrackingRegistration.AddRadarTracking(builder.Services);
WorkJournal.Web.Services.InvestmentInsightsRegistration.AddInvestmentInsights(builder.Services);
WorkJournal.Web.Services.StrategyRegistration.AddStrategyCenter(builder.Services, builder.Configuration);
var app = builder.Build();
app.UseMiddleware<TrustedOriginMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();
app.Run();

public partial class Program { }
