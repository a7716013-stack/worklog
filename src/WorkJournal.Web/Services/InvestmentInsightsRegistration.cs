using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
namespace WorkJournal.Web.Services;
public static class InvestmentInsightsRegistration
{
 public static IServiceCollection AddInvestmentInsights(this IServiceCollection services){
  services.TryAddSingleton(TimeProvider.System);services.AddHttpClient<MarketPulseProvider>(c=>{c.Timeout=TimeSpan.FromSeconds(20);c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 WorkJournal/1.4.2");});
  services.TryAddScoped<IMarketPulseProvider>(sp=>sp.GetRequiredService<MarketPulseProvider>());services.AddScoped<MarketPulseService>();services.AddScoped<PortfolioAnalyticsService>();services.AddHostedService<InvestmentInsightsScheduler>();return services;
 }
}
public class InvestmentInsightsScheduler(IServiceScopeFactory scopes,TimeProvider clock,IConfiguration config,ILogger<InvestmentInsightsScheduler> log):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct){
  if(!config.GetValue("InvestmentInsights:SchedulerEnabled",true))return;
  await Task.Delay(TimeSpan.FromSeconds(40),ct);
  while(!ct.IsCancellationRequested){try{var now=clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8));if(now.TimeOfDay>=new TimeSpan(13,40,0)&&now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)){
   using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<MarketPulseService>().CaptureAsync(ct);
   var db=scope.ServiceProvider.GetRequiredService<JournalDbContext>();var accounts=await db.PaperTradingAccounts.AsNoTracking().Where(x=>x.ApplicationUserId!=null).Select(x=>x.Id).ToListAsync(ct);
   foreach(var id in accounts)try{await scope.ServiceProvider.GetRequiredService<PortfolioAnalyticsService>().CaptureAsync(id,ct);}catch(Exception e) when(e is not OperationCanceledException){log.LogWarning("Portfolio capture deferred ({Type})",e.GetType().Name);}
  }}catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}catch(Exception e){log.LogWarning("Investment insights deferred ({Type})",e.GetType().Name);}await Task.Delay(TimeSpan.FromMinutes(15),ct);}
 }
}
