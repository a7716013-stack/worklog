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
builder.Services.AddScoped<WorkJournal.Web.Services.HomeDashboardService>();
builder.Services.AddHttpClient<WorkJournal.Web.Services.TaiwanMarketRankingProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 WorkJournal/1.3.1");
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
builder.Services.AddHttpClient<WorkJournal.Web.Services.EtfOfficialService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 WorkJournal/1.3.1");
});
builder.Services.AddOptions<WorkJournal.Web.Models.PaperTradingOptions>()
    .Bind(builder.Configuration.GetSection("PaperTrading"))
    .Validate(x => x.IsValid(), "虛擬交易參數無效。")
    .ValidateOnStart();
builder.Services.AddScoped<WorkJournal.Web.Services.IPaperTradingService, WorkJournal.Web.Services.PaperTradingService>();
WorkJournal.Web.Services.RadarTrackingRegistration.AddRadarTracking(builder.Services);
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
