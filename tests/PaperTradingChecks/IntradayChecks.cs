using System.Net;
using Microsoft.Extensions.Caching.Memory;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using WorkJournal.Web.ViewModels;
public static class IntradayChecks
{
    public static async Task Run()
    {
        var now = new DateTimeOffset(2026,10,15,10,12,0,TimeSpan.FromHours(8));
        void Check(bool value,string label) { if(!value)throw new Exception("FAIL: "+label); Console.WriteLine("PASS: "+label); }
        var json="""{"msgArray":[{"c":"2330","d":"20261015","t":"10:12:00","z":"-","trade":{"z":"2440","t":"10:11:30"}},{"c":"6488","d":"20261015","t":"10:12:00","z":"100"}]}""";
        var ticks=PaperIntradayService.Parse(json,now);
        Check(ticks.Length==2 && ticks[0].Price==2440 && ticks[0].At.Minute==11,"MIS explicit last trade timestamp and OTC quote parsed");
        Check(PaperIntradayService.Parse(json.Replace("20261015","20261014"),now).Length==0,"yesterday quotes are not intraday");
        Check(PaperIntradayService.Parse(json.Replace("20261015","20261016"),now).Length==0,"future quotes rejected");
        Check(PaperIntradayService.Parse("""{"msgArray":[{"c":"2330","d":"20261015","t":"10:12:00","z":"-","a":"2440","y":"2400"}]}""",now).Length==0,"bid ask and yesterday close never substitute for a trade");
        Check(PaperIntradayService.InSession(now) && !PaperIntradayService.InSession(now.AddDays(2)) && !PaperIntradayService.InSession(now.AddHours(4)),"session uses Taiwan time and excludes weekend and after hours");
        using var memory=new MemoryCache(new MemoryCacheOptions());
        var handler=new TickHandler(json); using var http=new HttpClient(handler);
        var service=new PaperIntradayService(http,memory,new FixedClock(now));
        PaperPortfolioViewModel Portfolio()=>new(){Account=new(){Cash=1000,InitialCash=2000},Positions=[new(new(){StockId="2330",StockName="台積電",Quantity=2,CostBasis=4000},new(){Symbol="2330",TradeDate=new(2026,10,14),CurrentPrice=2400})]};
        var p=Portfolio(); await service.ApplyAsync(p,default);
        Check(p.TotalAssets==5880 && p.UnrealizedProfitLoss==880 && p.Account.Cash==1000 && p.Trades.Count==0,"intraday valuation changes assets only and preserves ledger");
        await service.ApplyAsync(Portfolio(),default); Check(handler.Requests==1,"15-second batch cache prevents duplicate source requests");
        await service.ApplyAsync(Portfolio(),default,refresh:true); Check(handler.Requests==2,"manual refresh bypasses warm intraday cache");
        memory.Compact(1); handler.Fail=true; p=Portfolio(); await service.ApplyAsync(p,default);
        Check(p.Positions[0].CurrentPrice==2400 && p.Positions[0].QuoteTime==null && p.Positions[0].QuoteStatus.Contains("日行情參考"),"outage visibly falls back to daily reference without fake live timestamp");
    }
    sealed class FixedClock(DateTimeOffset now):TimeProvider {public override DateTimeOffset GetUtcNow()=>now;}
    sealed class TickHandler(string json):HttpMessageHandler
    {
        public int Requests; public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Requests++;return Task.FromResult(new HttpResponseMessage(Fail?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK){Content=new StringContent(json)});}
    }
}
