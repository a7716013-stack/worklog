using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using WorkJournal.Web.Security;
public static class StockResearchChecks
{
    private sealed record User(string Id):ICurrentUser;
    public static async Task RunAsync(DbContextOptions<JournalDbContext> options)
    {
        int count=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;Console.WriteLine("PASS: "+label);}
        var day=new DateOnly(2026,1,1);var stock=new SwingStock("2330","台積電","twse",false);
        var bars=Enumerable.Range(0,80).Select(i=>new SwingBar(day.AddDays(i),100+i,102+i,99+i,101+i,i==79?5000:1000)).ToArray();
        var flows=bars.Select(x=>new SwingFlow(x.Date,100,-50,25)).ToArray();
        var result=StockResearchCalculator.Calculate(new(stock,bars,flows,"test"));
        Check(result.Chart.Length==80,"research retains chart history beyond requested viewport");
        Check(result.Chart[^1].VolumeMA5==1000&&result.Chart[^1].VolumeMA20==1000,"volume average excludes current surge");
        Check(result.Chart[19].VolumeMA20==null&&result.Chart[20].VolumeMA20==1000,"volume needs full prior 20 sessions");
        Check(result.Flows[0].Days20==2000&&result.Flows[1].Days5==-250&&result.Flows[2].Day==25,"all three institutions retain signs and correct windows");
        Check(result.ShortTrend=="偏多"&&result.MediumTrend=="偏多","rising trend requires price and MA direction");
        var missing=StockResearchCalculator.Calculate(new(stock,bars,flows.SkipLast(1).ToArray(),""));
        Check(missing.Flows.All(x=>x.Day==null&&x.Days5==null&&x.Days20==null),"missing latest institution day not stale or zero");
        var gap=(SwingBar[])bars.Clone();gap[75]=gap[75] with{Volume=null};
        Check(StockResearchCalculator.Calculate(new(stock,gap,flows,"")).Chart[^1].VolumeMA5==null,"missing volume does not reduce average denominator");
        Check(StockResearchCalculator.Calculate(new(stock,[],[],"")).ShortTrend=="資料不足","empty history safe");
        await using(var db=new JournalDbContext(options)){
            var f=new StockFavoriteService(db,new User("alice"));var w=new StockWatchlistService(db,new User("alice"));
            await w.ChangeAsync([stock],null,default);await f.ChangeAsync([stock],null,default);
            await f.ChangeAsync([stock],null,default);Check((await f.GetAsync(default)).Count(x=>x.Symbol=="2330")==1,"favorite deduplication");
            await f.ChangeAsync([],"2330",default);Check((await w.GetAsync(default)).Any(x=>x.Symbol=="2330"),"favorite removal preserves swing");
            await f.ChangeAsync([stock],null,default);await w.ChangeAsync([],"2330",default);
            Check((await f.GetAsync(default)).Any(x=>x.Symbol=="2330"),"swing removal preserves favorite");
            Check(!(await new StockFavoriteService(db,new User("bob")).GetAsync(default)).Any(x=>x.Symbol=="2330"),"favorites isolated by account");
            var many=Enumerable.Range(7000,99).Select(i=>new SwingStock(i.ToString(),"fixture","twse",false)).ToArray();
            await f.ChangeAsync(many,null,default);Check((await f.GetAsync(default)).Count==100,"favorites support independent 100 capacity");
            try{await f.ChangeAsync([new("8888","excess","twse",false)],null,default);throw new Exception("limit missing");}catch(ValidationException){Check(true,"favorite capacity enforced atomically");}
        }
        // Verify the real upgrade from the previous schema, not only a new empty database.
        var name="WorkJournalFavoritesMigration_"+Guid.NewGuid().ToString("N");
        var opts=new DbContextOptionsBuilder<JournalDbContext>().UseSqlServer($@"Server=.\SQLEXPRESS;Database={name};Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;").Options;
        await using var migration=new JournalDbContext(opts);
        try {
            var all=migration.Database.GetMigrations().ToArray();
            await migration.GetService<IMigrator>().MigrateAsync(all[^2]);
            migration.Users.Add(new ApplicationUser{Id="existing",UserName="existing"});
            migration.StockWatchlistItems.Add(new(){ApplicationUserId="existing",Symbol="2330",Name="台積電",Market="twse"});await migration.SaveChangesAsync();
            await migration.Database.MigrateAsync();
            Check(await migration.StockFavoriteItems.CountAsync()==1&&await migration.StockWatchlistItems.CountAsync()==1,"upgrade copies existing swing without deleting it");
            await migration.Database.MigrateAsync();Check(await migration.StockFavoriteItems.CountAsync()==1,"repeated migration does not duplicate favorites");
        } finally {await migration.Database.EnsureDeletedAsync();}
        Console.WriteLine($"StockResearchChecks: {count} PASS");
    }
}
