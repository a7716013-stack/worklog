using Microsoft.Extensions.DependencyInjection.Extensions;
namespace WorkJournal.Web.Services;
public static class RadarTrackingRegistration
{
    public static IServiceCollection AddRadarTracking(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<RadarOfficialEventService>(client=>client.Timeout=TimeSpan.FromSeconds(15));
        services.TryAddScoped<IRadarTrackingDataProvider,RadarTrackingDataProvider>();
        services.AddScoped<MarketRadarRecommendationService>();
        services.AddScoped<MarketRadarPerformanceService>();
        services.AddScoped<MarketRadarStatisticsService>();
        services.AddScoped<RadarPaperTradingComparisonService>();
        services.AddHostedService<MarketRadarScheduler>();
        return services;
    }
}
