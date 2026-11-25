using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Security;
namespace WorkJournal.Api;
public static class LocalGuestBootstrap
{
    public static async Task Initialize(WebApplication app)
    {
        if (!LocalGuestHost.Enabled(app.Configuration)) throw new InvalidOperationException("Guest initialization requires isolated guest configuration.");
        using var scope=app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<JournalDbContext>().Database.MigrateAsync();
    }
}
