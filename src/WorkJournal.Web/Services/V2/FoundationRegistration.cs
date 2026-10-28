using Microsoft.Extensions.DependencyInjection;
namespace WorkJournal.Web.Services.V2;
public static class FoundationRegistration
{
    public static IServiceCollection AddV2Foundations(this IServiceCollection services)
    {
        services.AddScoped<FoundationService>();
        services.AddScoped<IInstrumentIdentityService>(s => s.GetRequiredService<FoundationService>());
        services.AddScoped<IDatasetSnapshotService>(s => s.GetRequiredService<FoundationService>());
        services.AddScoped<IUniverseSnapshotService>(s => s.GetRequiredService<FoundationService>());
        services.AddScoped<IVersionSetService>(s => s.GetRequiredService<FoundationService>());
        services.AddScoped<IParameterSetService>(s => s.GetRequiredService<FoundationService>());
        services.AddScoped<IDecisionTraceService>(s => s.GetRequiredService<FoundationService>());
        services.AddScoped<IPitQueryService>(s => s.GetRequiredService<FoundationService>());
        return services;
    }
}
