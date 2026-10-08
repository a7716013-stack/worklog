using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;

public static class PortfolioEnrollmentChecks
{
    public static async Task RunAsync(DbContextOptions<JournalDbContext> options,RadarClock clock,TrackingFixture source)
    {
        var passed=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);passed++;Console.WriteLine("PASS: "+message);}
        var quotes=new EnrollmentQuotes();var history=new EnrollmentHistory();
        async Task<long> Add(string owner,string symbol,string origin)
        {
            await using var db=new JournalDbContext(options);
            return await new MarketRadarRecommendationService(db,quotes,source,clock).FollowPortfolioSymbolAsync(owner,symbol,origin,history,default);
        }
        async Task Reject(Func<Task> action,string message){try{await action();throw new Exception("Expected rejection");}catch(ValidationException){Check(true,message);}}
        await using var setup=new JournalDbContext(options);
        setup.StockWatchlistItems.AddRange(new(){ApplicationUserId="alice",Symbol="9998",Name="Outside candidate",Market="twse"},new(){ApplicationUserId="alice",Symbol="9999",Name="Unavailable history",Market="twse"},new(){ApplicationUserId="alice",Symbol="9997",Name="Unavailable quote",Market="twse"});
        await setup.SaveChangesAsync();
        await Reject(()=>Add("bob","9998","swing"),"cannot enroll another owner's watchlist symbol");
        await Reject(()=>Add("alice","9998","paper"),"watchlist cannot impersonate paper-order source");
        await Reject(()=>Add("alice","9998","invalid"),"invalid enrollment origin rejected");
        await Reject(()=>Add("alice","<bad>","swing"),"invalid enrollment symbol rejected");
        var ids=await Task.WhenAll(Enumerable.Range(0,3).Select(_=>Add("alice","9998","swing")));
        Check(ids.Distinct().Count()==1,"concurrent enrollment uses one immutable snapshot");
        var id=ids[0];
        var before=await setup.MarketRadarRecommendations.AsNoTracking().SingleAsync(x=>x.Id==id);
        var joined=await setup.MarketRadarPersonalTrackings.AsNoTracking().SingleAsync(x=>x.RecommendationId==id);
        Check(before.Score is null&&before.Category=="Incomplete","missing history retains incomplete V1 score");
        Check(!quotes.Analyzed,"noncandidate enrollment never requests entire candidate analysis");
        Check(!await setup.MarketRadarDailySelections.AnyAsync(x=>x.RecommendationId==id),"private enrollment does not enter public daily selection");
        var account=await setup.PaperTradingAccounts.SingleAsync(x=>x.ApplicationUserId=="alice");
        var cash=account.Cash;
        setup.PaperOrders.Add(new(){AccountId=account.Id,StockId="9998",StockName="Outside candidate",Quantity=1,Status=PaperOrderStatus.Pending});
        await setup.SaveChangesAsync();
        Check(await Add("alice","9998","paper")==id,"paper and swing share same dated snapshot");
        await Reject(()=>Add("bob","9998","paper"),"cannot enroll another owner's paper symbol");
        Check(await setup.PaperOrders.CountAsync(x=>x.StockId=="9998"&&x.Status==PaperOrderStatus.Pending)==1&&await setup.PaperTradingAccounts.Where(x=>x.Id==account.Id).Select(x=>x.Cash).SingleAsync()==cash,"enrollment cannot fill orders or change cash");
        await using(var db=new JournalDbContext(options))
        {
            var service=new MarketRadarRecommendationService(db,quotes,source,clock);
            await service.StopAsync("alice",joined.Id,default);
        }
        await Add("alice","9998","swing");
        var after=await setup.MarketRadarPersonalTrackings.AsNoTracking().SingleAsync(x=>x.Id==joined.Id);
        Check(after.JoinedAt==joined.JoinedAt&&after.StoppedAt==null,"rejoin preserves personal start date");
        Check((await setup.MarketRadarRecommendations.AsNoTracking().SingleAsync(x=>x.Id==id)).SnapshotJson==before.SnapshotJson,"repeat submission preserves original snapshot");
        var stats=new MarketRadarStatisticsService(setup,clock);
        Check((await stats.StatisticsAsync(new(){Mine=true,Symbol="9998"},"alice",default)).Validation.Sum(x=>x.Count)==1,"enrollment visible in personal model validation");
        Check((await stats.StatisticsAsync(new(){Mine=true,Symbol="9998"},"bob",default)).Validation.Count==0,"personal model validation remains isolated");
        await Reject(()=>Add("alice","9997","swing"),"missing quote rejects enrollment");
        history.Fail=true;
        try{await Add("alice","9999","swing");throw new Exception("Expected provider failure");}catch(HttpRequestException){Check(true,"history provider failure propagated for safe UI message");}
        Check(!await setup.MarketRadarRecommendations.AnyAsync(x=>x.StockId=="9999"),"provider failure cannot create phantom snapshot");
        Console.WriteLine($"PortfolioEnrollmentChecks: {passed} PASS");
    }
}
public class EnrollmentHistory:IMarketRadarHistoryProvider
{
    public bool Fail;
    public Task<RadarHistory> GetAsync(MarketQuote quote,CancellationToken ct)=>Fail?throw new HttpRequestException("fixture failure"):Task.FromResult(new RadarHistory([],[],null,null));
}
public class EnrollmentQuotes:IMarketRadarService
{
    public bool Analyzed;
    public Task<MarketRadarResult> GetAsync(string market,DateOnly? date,bool analyze,CancellationToken ct)
    {
        Analyzed|=analyze;
        var day=RadarFixture.Day;
        return Task.FromResult(new MarketRadarResult(day,[day],market,[new(new("9998","Outside candidate","twse",false),day,100,2,1000),new(new("9999","Unavailable history","twse",false),day,100,2,1000)],[],[],[],DateTimeOffset.UtcNow));
    }
}
