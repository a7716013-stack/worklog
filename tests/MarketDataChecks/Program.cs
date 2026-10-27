using System.Net;
using System.Text;
using System.Text.Json;
using WorkJournal.MarketData;

static void Check(bool ok,string message) { if (!ok) throw new Exception(message); }
static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();
static MarketDataOptions Options() => new() { CacheDirectory = Path.GetFullPath(Path.Combine("artifacts","market-data-tests",Guid.NewGuid().ToString("N"))), OfficialIntervalMilliseconds = 0 };
static async Task MustFail(Func<Task> action,string message) { try { await action(); } catch (MarketDataException) { return; } throw new Exception(message); }
var clock = new TestClock();
var day = new DateOnly(2026,9,1);
const string month = """{"stat":"OK","date":"20260901","fields":["日期","成交股數","成交金額","開盤價","最高價","最低價","收盤價","漲跌價差"],"data":[["115/09/01","1,234,567","10000000","10","12","9","11","+1"]]}""";
var rows = OfficialParser.MonthlyPrices(Json(month),"2330","twse",day);
Check(rows[0].GetProperty("Trading_Volume").GetDecimal() == 1234567,"TWSE volume must retain shares");
const string otc = """{"stat":"ok","date":"20260901","tables":[{"fields":["日 期","成交張數","成交仟元","開盤","最高","最低","收盤","漲跌"],"data":[["115/09/01","1234","10000","10","12","9","11","1"]]}]}""";
Check(OfficialParser.MonthlyPrices(Json(otc),"6488","tpex",day)[0].GetProperty("Trading_Volume").ValueKind == JsonValueKind.Null,"Rounded lots must not become exact shares");
try { OfficialParser.MonthlyPrices(Json(month),"2330","twse",day.AddMonths(-1)); throw new Exception("Wrong month accepted"); } catch (InvalidDataException) { }
Check(OfficialParser.Flows(Json("""{"stat":"很抱歉，沒有符合條件的資料!"}"""),"twse",day).Length == 0,"Holiday response");
var headings = new[] {"代號","名稱"}.Concat(Enumerable.Range(0,7).SelectMany(_ => new[]{"買進股數","賣出股數","買賣超股數"})).Append("三大法人買賣超股數合計").ToArray();
var flowValues = new[]{"6488","Example"}.Concat(Enumerable.Range(0,7).SelectMany(i => new[]{(100+i).ToString(),"10",(90+i).ToString()})).Append("999").ToArray();
var flowRoot = JsonSerializer.SerializeToElement(new { date="20260901",tables=new[]{new {fields=headings,data=new[]{flowValues}}} });
var flows = OfficialParser.Flows(flowRoot,"tpex",day);
Check(flows[0].GetProperty("buy").GetDecimal()==100 && flows[1].GetProperty("buy").GetDecimal()==103 && flows[2].GetProperty("buy").GetDecimal()==106,"Foreign excludes foreign dealer; trust and aggregate dealer groups");

var options = Options(); var cache = new SourceCache(options,clock); int fetches=0;
async Task<JsonElement[]> Fetch() { Interlocked.Increment(ref fetches); await Task.Delay(10); return rows; }
await Task.WhenAll(Enumerable.Range(0,20).Select(_ => cache.GetAsync("same",TimeSpan.FromHours(1),Fetch,default)));
Check(fetches==1,"Concurrent cache misses must coalesce");
int sharedFetches=0;
async Task<JsonElement[]> SharedFetch() { Interlocked.Increment(ref sharedFetches); await Task.Delay(20); return rows; }
await Task.WhenAll(Enumerable.Range(0,4).Select(_ => new SourceCache(options,clock).GetAsync("shared-instance",TimeSpan.FromHours(1),SharedFetch,default)));
Check(sharedFetches==1,"Independent cache instances must share file lock");
cache = new SourceCache(options,clock);
await cache.GetAsync("same",TimeSpan.FromHours(1),Fetch,default);
Check(fetches==1,"Cache must survive restart");
clock.Now=clock.Now.AddHours(2); await cache.GetAsync("same",TimeSpan.FromHours(1),Fetch,default);
Check(fetches==2,"Expired cache must refresh");
var cts=new CancellationTokenSource();cts.Cancel();
try { await cache.GetAsync("cancel",TimeSpan.FromHours(1),Fetch,cts.Token); throw new Exception("Cancellation ignored"); } catch(OperationCanceledException) { }

options=Options();options.FinMindRequestsPerHour=1;
var budget=new RequestBudget(options,clock);await budget.AcquireAsync("FinMind",default);
await MustFail(()=>new RequestBudget(options,clock).AcquireAsync("FinMind",default),"Budget reset on restart");
var raceOptions=Options();raceOptions.FinMindRequestsPerHour=1;
async Task<bool> AcquireOnce() { try { await new RequestBudget(raceOptions,clock).AcquireAsync("FinMind",default);return true; } catch(MarketDataException){return false;} }
var race=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>AcquireOnce()));
Check(race.Count(x=>x)==1,"Independent budget instances must not overspend");
var reservedOptions=Options();reservedOptions.FinMindRequestsPerHour=2;reservedOptions.InteractiveFinMindReserve=1;
var reservedBudget=new RequestBudget(reservedOptions,clock);
using(MarketDataWork.BackgroundScope())
{
    await reservedBudget.AcquireAsync("FinMind",default);
    await MustFail(()=>reservedBudget.AcquireAsync("FinMind",default),"Background consumed interactive reserve");
}
Check(!MarketDataWork.IsBackground,"Background scope leaked into foreground");
await reservedBudget.AcquireAsync("FinMind",default);
clock.Now=clock.Now.AddHours(2);await budget.AcquireAsync("FinMind",default);
await budget.BlockAsync("Official",TimeSpan.FromMinutes(15),default);
await MustFail(()=>budget.AcquireAsync("Official",default),"Cooldown ignored");

// Exercise full routing: official catalog + monthly cache, overlapping requests, no FinMind calls.
options=Options();var handler=new FixtureHandler(month);using var http=new HttpClient(handler);
cache=new SourceCache(options,clock);budget=new RequestBudget(options,clock);
var gateway=new MarketDataGateway(http,cache,budget,clock);int backupCalls=0;
Task<JsonElement[]> Backup() { backupCalls++; return Task.FromResult(rows); }
var a=await gateway.ReadAsync("TaiwanStockPrice","2330",day,day.AddDays(10),Backup,default);
var b=await gateway.ReadAsync("TaiwanStockPrice","2330",day,day.AddDays(20),Backup,default);
Check(a.Length==1 && b.Length==1 && backupCalls==0 && handler.Calls==3,"Official monthly data reused across ranges");
gateway=new MarketDataGateway(http,new SourceCache(options,clock),new RequestBudget(options,clock),clock);
await gateway.ReadAsync("TaiwanStockPrice","2330",day,day.AddDays(20),Backup,default);
Check(handler.Calls==3,"Gateway cache survives restart");

options=Options();gateway=new MarketDataGateway(http,new SourceCache(options,clock),new RequestBudget(options,clock),clock);
int quotaCalls=0;
Task<JsonElement[]> Quota() { quotaCalls++; throw new HttpRequestException("quota",null,HttpStatusCode.PaymentRequired); }
await MustFail(()=>gateway.ReadAsync("TaiwanStockPER","2330",day,day,Quota,default),"Quota error not exposed");
await MustFail(()=>gateway.ReadAsync("TaiwanStockPER","2317",day,day,Quota,default),"Cooldown must cover all symbols");
Check(quotaCalls==1,"Provider must not be retried during cooldown");
Console.WriteLine("PASS: official schemas, exact/missing volume, date validation, holiday, institutional groups, coalescing, restart cache, expiry, cancellation, persisted budget, shared cooldown and routing.");

if (args.Contains("--live"))
{
    var liveOptions=new MarketDataOptions { CacheDirectory=Path.GetFullPath("src/WorkJournal.Web/App_Data/market-data") };
    var liveCache=new SourceCache(liveOptions,TimeProvider.System);
    using var liveHttp=new HttpClient { Timeout=TimeSpan.FromSeconds(25) };
    var live=new MarketDataGateway(liveHttp,liveCache,new RequestBudget(liveOptions,TimeProvider.System),TimeProvider.System);
    Task<JsonElement[]> NoBackup()=>throw new MarketDataException("Live check disables FinMind to verify independent official sources.");
    var end=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(-1);
    foreach(var symbol in new[]{"2330","6488"})
    {
        var start=new DateOnly(end.Year,end.Month,1);
        var prices=await live.ReadAsync("TaiwanStockPrice",symbol,start,end,NoBackup,default);
        var institutional=await live.ReadAsync("TaiwanStockInstitutionalInvestorsBuySell",symbol,end.AddDays(-3),end,NoBackup,default);
        Check(prices.Length>0 && institutional.Length>0,"Live source empty");
        Console.WriteLine($"LIVE {symbol}: prices={prices.Length}; flows={institutional.Length}; source={OfficialParser.Text(prices[^1],"_source")}; date={OfficialParser.Text(prices[^1],"date")}");
    }
    var before=liveCache.Fetches;
    await live.ReadAsync("TaiwanStockPrice","2330",new DateOnly(end.Year,end.Month,1),end,NoBackup,default);
    Check(before==liveCache.Fetches,"Live cached repeat used upstream");
    Console.WriteLine($"LIVE PASS: persistent hits={liveCache.Hits}; fetched blocks={liveCache.Fetches}; repeated query adds zero requests.");
}

sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now {get;set;}=new(2026,10,23,0,0,0,TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow()=>Now;
}
sealed class FixtureHandler(string month) : HttpMessageHandler
{
    public int Calls {get;private set;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
    {
        Calls++;
        if (r.Headers.Authorization!=null) throw new Exception("Credentials leaked to official host");
        var isCatalog=r.RequestUri!.Host=="isin.twse.com.tw";
        var symbol=r.RequestUri.Query.Contains("strMode=4")?"6488":"2330";
        var payload=isCatalog?$"<tr><td>{symbol} Example</td><td>TW0002330008</td><td>2000/01/01</td><td>market</td><td>Industry</td><td>ESVUFR</td><td></td></tr>":month;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(payload,Encoding.UTF8,isCatalog?"text/html":"application/json")});
    }
}
