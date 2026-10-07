using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
namespace WorkJournal.Web.Services;
public class MarketRadarScheduler(IServiceScopeFactory scopes,TimeProvider clock,IConfiguration config,ILogger<MarketRadarScheduler> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!config.GetValue("RadarTracking:SchedulerEnabled",true))return;
        await Task.Delay(TimeSpan.FromSeconds(30),clock,stoppingToken);
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now=clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8));
                if(now.TimeOfDay>=new TimeSpan(13,30,0)&&now.TimeOfDay<=new TimeSpan(22,0,0))
                {
                    using var scope=scopes.CreateScope();
                    var radar=scope.ServiceProvider.GetRequiredService<IMarketRadarService>();var day=DateOnly.FromDateTime(now.DateTime);
                    var snapshot=await radar.GetAsync("all",day,false,stoppingToken);
                    // Current-day official quotes confirm an actual session; closures never trigger capture or performance work.
                    if(new[]{"twse","tpex"}.All(m=>snapshot.Quotes.Any(x=>x.Date==day&&x.Stock.Market==m)))
                    {
                        await scope.ServiceProvider.GetRequiredService<MarketRadarRecommendationService>().CaptureDailyAsync(stoppingToken);
                        if(now.Hour>=18)await scope.ServiceProvider.GetRequiredService<MarketRadarPerformanceService>().UpdatePendingAsync(stoppingToken);
                    }
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogWarning("Radar scheduled check deferred ({Type})",e.GetType().Name);}
            var ticks=clock.GetUtcNow().Ticks;var interval=TimeSpan.FromMinutes(15).Ticks;
            await Task.Delay(TimeSpan.FromTicks(interval-ticks%interval),clock,stoppingToken);
        }
    }
}
