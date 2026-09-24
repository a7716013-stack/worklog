using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkJournal.Web.Controllers;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;

var database = "WorkJournalPaperChecks_" + Guid.NewGuid().ToString("N");
var connection = $@"Server=.\SQLEXPRESS;Database={database};Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;";
var dbOptions = new DbContextOptionsBuilder<JournalDbContext>().UseSqlServer(connection, sql => sql.EnableRetryOnFailure()).Options;
await using var setup = new JournalDbContext(dbOptions);
await setup.Database.MigrateAsync();
Console.WriteLine("Isolated SQL database: " + database);
try
{
    if (args.Contains("--serve"))
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo != null && !File.Exists(Path.Combine(repo.FullName, "WorkJournal.sln"))) repo = repo.Parent;
        var webRoot = Path.Combine(repo?.FullName ?? throw new InvalidOperationException("Repository not found"), "src/WorkJournal.Web");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            ContentRootPath = webRoot, WebRootPath = Path.Combine(webRoot, "wwwroot"),
            ApplicationName = typeof(StockAnalysisController).Assembly.GetName().Name, EnvironmentName = "Development"
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls("http://localhost:5187");
        builder.Services.AddControllersWithViews(o => {
            o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            var messages = o.ModelBindingMessageProvider;
            messages.SetValueIsInvalidAccessor(value => $"輸入值「{value}」無效。");
            messages.SetAttemptedValueIsInvalidAccessor((value, field) => $"{field} 的值「{value}」無效。");
            messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"輸入值「{value}」無效。");
            messages.SetValueMustNotBeNullAccessor(field => "請填寫此欄位。");
            messages.SetValueMustBeANumberAccessor(field => $"{field} 必須為數字。");
        })
            .AddApplicationPart(typeof(StockAnalysisController).Assembly);
        builder.Services.AddDbContext<JournalDbContext>(o => o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure()));
        builder.Services.AddMemoryCache();
        builder.Services.Configure<PaperTradingOptions>(_ => { });
        builder.Services.AddScoped<IPaperTradingService, PaperTradingService>();
        var market = new FixtureMarket();
        builder.Services.AddScoped(sp => new FinMindStockService(new HttpClient(market, false) { BaseAddress = new Uri("https://fixture.test/") },
            sp.GetRequiredService<IMemoryCache>(), new ConfigurationBuilder().Build(), NullLogger<FinMindStockService>.Instance));
        var app = builder.Build();
        app.Use(async (context, next) => { context.Response.Headers["X-PaperTrading-Fixture"] = "isolated-sql"; await next(); });
        app.UseStaticFiles();
        app.UseRouting();
        app.MapControllerRoute("default", "{controller=WorkLogs}/{action=Index}/{id?}");
        app.MapPost("/__test/stop", async (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) => {
            await antiforgery.ValidateRequestAsync(context);
            app.Lifetime.StopApplication();
            return Results.Ok();
        });
        Console.WriteLine("Fixture web ready: http://localhost:5187 (synthetic quotes; isolated SQL only)");
        await app.RunAsync();
    }
    else await DomainChecks.Run(dbOptions);
}
finally
{
    // Only the GUID-named database created by THIS run may be removed.
    if (!database.StartsWith("WorkJournalPaperChecks_") || database.Length != "WorkJournalPaperChecks_".Length + 32)
        throw new InvalidOperationException("Unexpected test database name");
    await setup.Database.EnsureDeletedAsync();
    Console.WriteLine("Isolated test database removed.");
}
