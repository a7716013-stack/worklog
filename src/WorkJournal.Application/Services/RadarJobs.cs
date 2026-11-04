using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services;

public sealed record RadarJobView(string JobId,string Status,int Completed,int Total,DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,string ModelVersion,string? DataVersion,MarketRadarResult? Result,string? ErrorCode);
public sealed record RadarJobOptions
{
    public TimeSpan JobTimeout {get;init;}=TimeSpan.FromMinutes(3);
    public TimeSpan ItemTimeout {get;init;}=TimeSpan.FromSeconds(35);
}

// Public market data only. No owner/session/personal data is captured by a shared job.
// In-memory jobs expire after restart: unknown ids return 404; the UI explicitly offers a retry.
public sealed class RadarJobs(IServiceScopeFactory scopes,TimeProvider clock,ILogger<RadarJobs> logger,RadarJobOptions options) : BackgroundService
{
    private sealed class Job(string key,string market,DateOnly? date,DateTimeOffset now)
    {
        public readonly object Sync=new();
        public string Key=key,Market=market; public DateOnly? Date=date;
        public RadarJobView View=new(Guid.NewGuid().ToString("N"),"QUEUED",0,0,now,now.AddMinutes(15),"LegacyRadar100",null,null,null);
    }
    private readonly object sync=new();
    private readonly Dictionary<string,Job> jobs=[];
    private readonly Channel<Job> queue=Channel.CreateBounded<Job>(new BoundedChannelOptions(8){FullMode=BoundedChannelFullMode.Wait,SingleReader=true});
    public RadarJobView Start(string market,DateOnly? date)
    {
        if(market is not ("all" or "twse" or "tpex" or "etf"))throw new ArgumentException("market");
        var now=clock.GetUtcNow();
        var day=DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(8)).DateTime);
        if(date>day)throw new ArgumentException("date");
        // Provider daily snapshot and history caches bound freshness to 5/15 minutes.
        var key=$"{market}:{date ?? day}:LegacyRadar100:{now.ToUnixTimeSeconds()/300}";
        lock(sync)
        {
            foreach(var id in jobs.Where(x=>x.Value.View.ExpiresAt<=now).Select(x=>x.Key).ToArray())jobs.Remove(id);
            var existing=jobs.Values.FirstOrDefault(x=>x.Key==key);
            if(existing is not null) {lock(existing.Sync)return existing.View;}
            if(jobs.Count>=64)throw new InvalidOperationException("QUEUE_FULL");
            var job=new Job(key,market,date,now);
            if(!queue.Writer.TryWrite(job))throw new InvalidOperationException("QUEUE_FULL");
            jobs.Add(job.View.JobId,job);return job.View;
        }
    }
    public RadarJobView? Get(string id)
    {
        lock(sync) {if(!jobs.TryGetValue(id,out var job))return null;lock(job.Sync)return job.View.ExpiresAt>clock.GetUtcNow()?job.View:null;}
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try {await foreach(var job in queue.Reader.ReadAllAsync(stoppingToken))await Run(job,stoppingToken);}
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
    }
    private async Task Run(Job job,CancellationToken stop)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop);
        // Includes queue wait. Never leave a job RUNNING indefinitely.
        var remaining=job.View.CreatedAt.Add(options.JobTimeout)-clock.GetUtcNow();
        deadline.CancelAfter(remaining>TimeSpan.Zero?remaining:TimeSpan.Zero);
        var ct=deadline.Token;
        try
        {
            using var scope=scopes.CreateScope();
            var service=scope.ServiceProvider.GetRequiredService<IMarketRadarService>();
            lock(job.Sync)job.View=job.View with {Status="RUNNING"};
            var basic=await service.GetAsync(job.Market,job.Date,false,ct).WaitAsync(ct);
            var candidates=MarketRadarService.Candidates(basic.Quotes);
            lock(job.Sync)job.View=job.View with {Total=candidates.Length,Result=basic,DataVersion=ResearchMath.Digest(new {basic.TradeDate,basic.Quotes,basic.Events})};
            var completed=new ConcurrentDictionary<string,MarketRadarStock>();
            await Parallel.ForEachAsync(candidates,new ParallelOptions {MaxDegreeOfParallelism=4,CancellationToken=ct},async (quote,token)=>
            {
                using var itemScope=scopes.CreateScope();
                var analyzer=itemScope.ServiceProvider.GetRequiredService<MarketRadarService>();
                using var itemDeadline=CancellationTokenSource.CreateLinkedTokenSource(token);
                itemDeadline.CancelAfter(options.ItemTimeout);
                MarketRadarStock result;
                try {result=await analyzer.AnalyzeAsync(quote,basic.Events,itemDeadline.Token).WaitAsync(itemDeadline.Token);}
                catch(OperationCanceledException) when(!token.IsCancellationRequested)
                {result=MarketRadarScoreCalculator.Calculate(quote,new([],[],null,null),basic.Events);}
                completed[quote.Stock.Symbol]=result;
                lock(job.Sync)job.View=job.View with {Completed=completed.Count,Result=basic with {Analysis=completed.Values.OrderBy(x=>x.Quote.Stock.Symbol).ToArray()}};
            });
            lock(job.Sync)
            {
                var incomplete=job.View.Result!.Analysis.Any(x=>x.Score is null);
                job.View=job.View with {Status="COMPLETED",Result=job.View.Result with {Warnings=basic.Warnings.Concat(incomplete?["部分候選資料不足或來源逾時；缺項不補零、不換算百​​分。"]:Array.Empty<string>()).ToArray()}};
            }
        }
        catch(OperationCanceledException) {lock(job.Sync)job.View=job.View with {Status=stop.IsCancellationRequested?"INTERRUPTED":"TIMED_OUT",ErrorCode=stop.IsCancellationRequested?"WORKER_STOPPED":"ANALYSIS_TIMEOUT"};}
        catch(Exception e)
        {logger.LogWarning("Radar job failed {Type}",e.GetType().Name);lock(job.Sync)job.View=job.View with {Status="FAILED",ErrorCode="UPSTREAM_UNAVAILABLE"};}
        logger.LogInformation("Radar job {Id} {Status} {Completed}/{Total}",job.View.JobId,job.View.Status,job.View.Completed,job.View.Total);
    }
}
