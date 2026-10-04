using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkJournal.Web.Data;
using WorkJournal.Web.Security;
namespace AuthCalendarChecks;

public class FixtureFactory(string connection, FakeGoogle google) : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "WorkJournal.sln"))) repo = repo.Parent;
        builder.UseContentRoot(Path.Combine(repo?.FullName ?? throw new InvalidOperationException("Repository not found"), "src", "WorkJournal.Web"));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<JournalDbContext>>();
            services.AddDbContext<JournalDbContext>(options => options.UseSqlServer(connection));
            foreach (var scheme in new[] { GoogleAuthSettings.LoginScheme, GoogleAuthSettings.CalendarScheme })
                services.Configure<GoogleOptions>(scheme, options => options.Backchannel = new HttpClient(google, false));
            services.AddHttpClient("GoogleCalendarApi").ConfigurePrimaryHttpMessageHandler(() => google);
            services.AddLogging(logging => logging.ClearProviders());
            services.AddScoped(sp => new WorkJournal.Web.Services.FinMindStockService(
                new HttpClient(new FixtureMarket()) { BaseAddress = new Uri("https://fixture.test/") },
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), new ConfigurationBuilder().Build(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkJournal.Web.Services.FinMindStockService>.Instance));
        });
    }
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost:7180"), AllowAutoRedirect = false });
}
