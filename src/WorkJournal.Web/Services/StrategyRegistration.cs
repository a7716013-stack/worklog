using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public static class StrategyRegistration
{
    public static IServiceCollection AddStrategyCenter(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<StrategyScanOptions>().Bind(config.GetSection("StrategyCenter"))
            .Validate(x => x.Profile.IsValid() && x.BatchSize is >= 1 and <= 100 && x.RequestsPerHour is >= 10 and <= 600 && x.IntervalMinutes is >= 1 and <= 120 && x.CutoffHour is >= 18 and <= 23, "策略研究參數無效").ValidateOnStart();
        services.AddHttpClient("StrategyOfficial", c => { c.Timeout = TimeSpan.FromSeconds(25); c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 WorkJournal/1.5.1"); });
        services.AddScoped<IStockUniverseService, StockUniverseService>();
        services.AddScoped<IStrategyDataProvider, StrategyDataProvider>();
        services.AddScoped<IStrategySignalSnapshotService, StrategySignalSnapshotService>();
        services.AddScoped<StrategySignalService>(); services.AddScoped<StrategyScanService>();
        services.AddScoped<IStrategyPerformanceService, StrategyPerformanceService>();
        services.AddHostedService<StrategyScheduler>(); return services;
    }
}
public class StrategyScheduler(IServiceScopeFactory scopes, Microsoft.Extensions.Options.IOptions<StrategyScanOptions> options,
    ILogger<StrategyScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        await Task.Delay(TimeSpan.FromSeconds(45), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var dataBudget = WorkJournal.MarketData.MarketDataWork.BackgroundScope();
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IStrategyDataProvider>().CaptureEventsAsync(ct);
                await scope.ServiceProvider.GetRequiredService<StrategyScanService>().RunAsync(ct);
                await scope.ServiceProvider.GetRequiredService<IStrategyPerformanceService>().CalculatePendingPerformanceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning("Strategy background work deferred ({Type})", e.GetType().Name); }
            await Task.Delay(TimeSpan.FromMinutes(options.Value.IntervalMinutes), ct);
        }
    }
}
