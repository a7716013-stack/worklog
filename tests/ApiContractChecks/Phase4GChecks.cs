using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WorkJournal.Api.Services;
using WorkJournal.Domain.Research;
using WorkJournal.MarketData;
using WorkJournal.Web.Models;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.Research;

public static class Phase4GChecks
{
    public static void MapToken(WebApplication app)=>app.MapGet("/api/v1/test/token",(HttpContext c,IAntiforgery a)=>new {token=a.GetAndStoreTokens(c).RequestToken});
    public static async Task Run(HttpClient http,Action<bool,string> check)
    {
        var tokenResponse=await http.GetAsync("/api/v1/test/token");
        var token=JsonNode.Parse(await tokenResponse.Content.ReadAsStringAsync())!["token"]!.GetValue<string>();
        http.DefaultRequestHeaders.Add("Cookie",string.Join("; ",tokenResponse.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0])));
        var noCsrf=await http.PostAsync("/api/v1/market/radar/jobs",null);check(noCsrf.StatusCode==HttpStatusCode.BadRequest,"jobs require CSRF");
        http.DefaultRequestHeaders.Add("RequestVerificationToken",token);
        var clock=Stopwatch.StartNew();
        var response=await http.PostAsync("/api/v1/market/radar/jobs?market=all",null);
        var job=JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        check(response.StatusCode==HttpStatusCode.Accepted&&clock.Elapsed<TimeSpan.FromSeconds(2),"job accepted without provider wait");
        var repeat=JsonNode.Parse(await (await http.PostAsync("/api/v1/market/radar/jobs?market=all",null)).Content.ReadAsStringAsync())!;
        check(job["jobId"]!.ToString()==repeat["jobId"]!.ToString(),"same input coalesces jobs");
        check((await http.GetAsync("/api/v1/market/radar/jobs/unknown")).StatusCode==HttpStatusCode.NotFound,"restart/expired job is explicit 404");
        var bad=await http.PostAsync("/api/v1/market/radar/jobs?market=bad",null);check(bad.StatusCode==HttpStatusCode.BadRequest,"job input validation");
        var features=JsonNode.Parse(await http.GetStringAsync("/api/v1/research/features"))!;
        check(features["totalCount"]!.GetValue<int>()==19,"19 feature definitions");
        var validation=JsonNode.Parse(await http.GetStringAsync("/api/v1/research/features/Trend.Alignment/validation"))!;
        check(validation["status"]!.ToString()=="NOT_RUN"&&validation["value"] is null,"not run is null, not zero");
        foreach(var path in new[]{"candidates","hypotheses","calibrations","runs/REAL_BASELINE_V1/ablations","runs/REAL_BASELINE_V1/correlations","runs/REAL_BASELINE_V1/stability?period=quarter"})
            check((await http.GetAsync("/api/v1/research/"+path)).IsSuccessStatusCode,"research resource "+path);
        var id=V3CandidateCalculator.Version;
        foreach(var path in new[]{"definition","parameters","validation","promotion-status","promotion-review","freeze-receipt","research-package"})
            check((await http.GetAsync($"/api/v1/models/{id}/{path}")).IsSuccessStatusCode,"model resource "+path);
        check((await http.GetAsync("/api/v1/research/candidates/V3EvidenceCandidate-v2")).StatusCode==HttpStatusCode.NotFound,"no invented v2");
        var (data,p,instrument)=Fixture();
        var payload=new CanonicalComputeRequest(data,instrument,p);
        var compute=await http.PostAsJsonAsync($"/api/v1/models/{id}/compute",payload);
        check(compute.IsSuccessStatusCode,"canonical formula HTTP compute");
        var actual=await compute.Content.ReadFromJsonAsync<CanonicalComputeResult>();
        var expected=new V3CandidateCalculator().Calculate(data,instrument,p);
        check(actual!.Trace.Score.TotalScore==expected.Score.TotalScore&&!actual.ProductionEligible,"HTTP equals shared pure calculator, not promoted");
        check(actual.CalculationIdentity.FormulaDigest==FormulaCatalog.Identity.FormulaDigest&&actual.CalculationIdentity.FormulaDigest.Length==64,"formula digest traces executable source");
        check(actual.Trace.Components.Length==6&&actual.Trace.Components.Sum(x=>x.Features.Length)==19,"six component formula trace");
        var again=await (await http.PostAsJsonAsync($"/api/v1/models/{id}/compute",payload)).Content.ReadFromJsonAsync<CanonicalComputeResult>();
        check(actual.CalculationIdentity==again!.CalculationIdentity,"deterministic HTTP calculation identity");
        foreach(var (value,total) in new[]{(0m,0m),(.5m,50m),(1m,100m)}) {
            var e=Enum.GetValues<EvidenceCode>().Select(c=>new NormalizedEvidence(c,value,EvidenceStatus.Available,"TEST",id,id)).ToArray();
            var score=new FullEvidenceScoreCalculator().Calculate(e);check(score.TotalScore==total,"aggregate golden "+total);
            var viaHttp=await (await http.PostAsJsonAsync($"/api/v1/models/{id}/aggregate",new AggregationRequest(e))).Content.ReadFromJsonAsync<AggregationResult>();
            check(viaHttp!.Score.TotalScore==total,"HTTP aggregate golden "+total);
        }
        var mixed=new[]{1m,0m,0m,1m,.8m,0m};
        check(new FullEvidenceScoreCalculator().Calculate(Enum.GetValues<EvidenceCode>().Select((c,i)=>new NormalizedEvidence(c,mixed[i],EvidenceStatus.Available,"TEST",id,id))).TotalScore==57,"mixed golden 57");
        var mixedHttp=await (await http.PostAsJsonAsync($"/api/v1/models/{id}/aggregate",new AggregationRequest(Enum.GetValues<EvidenceCode>().Select((c,i)=>new NormalizedEvidence(c,mixed[i],EvidenceStatus.Available,"TEST",id,id)).ToArray()))).Content.ReadFromJsonAsync<AggregationResult>();
        check(mixedHttp!.Score.TotalScore==57,"HTTP mixed golden 57");
        var d=new DateOnly(2020,1,1);var rows=new[]{new ResearchValue(d,Guid.NewGuid(),0,0,null),new(d,Guid.NewGuid(),.5m,.5m,null),new(d,Guid.NewGuid(),1,1,null),new(d,Guid.NewGuid(),null,2,null)};
        var diag=FormulaDiagnostics.Validate("fixture",rows,1,5,true);
        check(diag.Ic==1&&diag.RankIc==1&&diag.MissingCount==1&&diag.Distribution.MissingRate==.25m,"diagnostics IC/missingness");
        check(diag.SaturationLow==1m/3&&diag.SaturationHigh==1m/3&&diag.YearlyIc.Length==1&&diag.QuarterlyIc.Length==1,"saturation/year/quarter");
        check(FormulaDiagnostics.Validate("empty",[],1,5,true).Ic is null,"empty IC remains null");
        check(FormulaDiagnostics.Validate("constant",rows.Select(x=>x with {Value=1}),1,5,true).Ic is null,"constant daily correlation remains null");
        var observation=new ResearchObservation(Guid.NewGuid(),expected,[new(1,d.AddDays(1),.1m,.2m,-.1m,"AVAILABLE")]);
        var package=FormulaDiagnostics.Build([observation],1,5,"SYNTHETIC_ONLY");
        var without=package.Ablations.Single(x=>x.RemovedEvidence=="Institutional");
        check(without.AvailableWeight==80&&without.Metrics.Distribution.Mean==expected.Score.TotalScore-expected.Components.Single(x=>x.Evidence.Code==EvidenceCode.Institutional).Evidence.Value*20,"ablation subtracts, never redistributes");
        check(package.Features.Length==19&&package.Correlations.Length==325,"feature and evidence diagnostics matrix");
        try {CandidateGovernance.Calibrate("x","p","c",new(d,d.AddDays(10)),DateTimeOffset.UtcNow,d.AddDays(5),"Linear","u");throw new Exception("calibration accepted leakage");}
        catch(ArgumentException){check(true,"reject full sample/validation calibration");}
        var risk=RiskDiagnostics.Labels(100,d,[d.AddDays(1),d.AddDays(2)],[new(d.AddDays(1),110,1000),new(d.AddDays(2),99,1000)]);
        check(risk.Drawdown==.1m&&risk.Volatility==.1m&&risk.LiquidityStability==0,"future risk labels retain separate meanings");
        check(RiskDiagnostics.Labels(100,d,[d.AddDays(1),d.AddDays(2)],[new(d.AddDays(1),110,null)]).Volatility is null,"missing future risk labels not zero");
        var now=new DateTimeOffset(2020,1,10,0,0,0,TimeSpan.Zero);
        var identity=new ResearchIdentity(id,"digest","parameters","checksum","TEST_APPROVAL","dataset","universe","benchmark","calendar","availability","protocol","commit");
        var h=new CandidateHypothesis("hypothesis","Trend","saturation","SYNTHETIC_TEST_ONLY","small family","expected","overfit","development",now,true);
        var metrics=new ComparisonEvidence(1,.1m,.1m,1,1,.1m,.5m,1);
        var comparison=new CandidateComparison("comparison",h.CandidateHypothesisId,id,"SYNTHETIC_TEST_CANDIDATE",identity,metrics,metrics,"development",now.AddDays(1),"KEEP_V1","Multi-evidence test review, not real approval");
        var freeze=CandidateGovernance.Freeze(h,comparison,now.AddDays(2),new(2020,2,1));
        check(freeze.CandidateVersion==id&&freeze.ReceiptDigest.Length==64,"keep baseline and deterministic freeze receipt");
        try {CandidateGovernance.Freeze(h with {CreatedAt=now.AddDays(3)},comparison,now.AddDays(2),new(2020,2,1));throw new Exception("late hypothesis accepted");}catch(ArgumentException){check(true,"hypothesis must precede evaluation");}
        try {CandidateGovernance.Freeze(h,comparison,now.AddMonths(1),new(2020,2,1));throw new Exception("late freeze accepted");}catch(ArgumentException){check(true,"freeze cannot follow OOS start");}
        await Cache(check);
        await SlowJobs(check);
    }
    private static async Task Cache(Action<bool,string> check)
    {
        var directory=Path.Combine(Path.GetTempPath(),"worklog-cache-"+Guid.NewGuid().ToString("N"));
        var cache=new SourceCache(new(){CacheDirectory=directory},TimeProvider.System);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int calls=0;
        async Task<JsonElement[]> Slow(){Interlocked.Increment(ref calls);entered.SetResult();await release.Task;return [];}
        var slow=cache.GetAsync("slow",TimeSpan.FromMinutes(1),Slow,default);await entered.Task;
        var fast=cache.GetAsync("fast",TimeSpan.FromMinutes(1),()=>Task.FromResult(Array.Empty<JsonElement>()),default);
        await fast.WaitAsync(TimeSpan.FromSeconds(2));check(true,"unrelated cache fetch not blocked by slow source");
        var duplicate=cache.GetAsync("slow",TimeSpan.FromMinutes(1),()=>{Interlocked.Increment(ref calls);return Task.FromResult(Array.Empty<JsonElement>());},default);
        release.SetResult();await Task.WhenAll(slow,duplicate);check(calls==1&&cache.Hits==1,"cross-call same key downloads once");
        // Only delete this test-created, fully qualified temporary directory.
        Directory.Delete(directory,true);
    }
    public static (CandidateDataset,V3EvidenceCandidateV1ParameterSet,Guid) Fixture()
    {
        var b=new LinearBounds(-1,1);
        var p=new V3EvidenceCandidateV1ParameterSet {SlopeHorizon=5,StructureHorizon=10,AlignmentBounds=b,PositionBounds=b,SlopeBounds=b,RocHorizon=5,MacdBounds=b,RocBounds=b,VolumeHorizon=5,ReturnHorizon=5,BreakoutHorizon=5,RvolLogBounds=b,PvcBounds=b,BreakoutBounds=b,InstitutionalHorizons=[2,3,5],PersistenceHorizon=5,Persistence=PersistenceGroup.Combined,RelativeStrengthHorizons=[2,3,5],VolatilityHorizon=5,Variance=VarianceDefinition.Population,DrawdownHorizon=5,AtrPeriod=5,Atr=AtrDefinition.SimpleMean,LiquidityHorizon=5,LiquidityPrice="Close"};
        var stock=Guid.Parse("10000000-0000-0000-0000-000000000001");var peer=Guid.Parse("10000000-0000-0000-0000-000000000002");
        var sessions=Enumerable.Range(0,140).Select(i=>new DateOnly(2020,1,1).AddDays(i)).ToImmutableArray();var date=sessions[^1];
        var cutoff=new DateTimeOffset(date.ToDateTime(new TimeOnly(23,0)),TimeSpan.Zero);
        var bars=sessions.Select(d=>new CandidateBar(d,100,101,99,1000,new DateTimeOffset(d.ToDateTime(new TimeOnly(16,0)),TimeSpan.Zero))).ToImmutableArray();
        var flows=bars.Select(b=>new CandidateFlow(b.Date,10,5,1,b.AvailableAt)).ToImmutableArray();
        var id=new CandidateIdentity(stock,peer,stock,peer,stock,V3CandidateCalculator.ParameterChecksum(p),"SYNTHETIC_DATASET","SYNTHETIC_UNIVERSE","SYNTHETIC_BENCHMARK","SYNTHETIC_PRICE",date,cutoff);
        return (new(id,FoundationStatus.Finalized,cutoff.AddHours(-1),cutoff.AddDays(-150),sessions,[stock,peer],[new(stock,bars,flows),new(peer,bars,flows)],bars),p,stock);
    }
    private static async Task SlowJobs(Action<bool,string> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddMemoryCache();
        services.AddScoped<IMarketRadarService,StubRadar>();services.AddScoped<ITaiwanMarketRankingProvider,UnusedRanking>();
        services.AddScoped<IMarketRadarHistoryProvider,HangingHistory>();services.AddScoped<MarketRadarService>();
        await using var provider=services.BuildServiceProvider();
        foreach(var whole in new[]{false,true})
        {
            using var jobs=new RadarJobs(provider.GetRequiredService<IServiceScopeFactory>(),TimeProvider.System,NullLogger<RadarJobs>.Instance,
                new(){ItemTimeout=whole?TimeSpan.FromSeconds(5):TimeSpan.FromMilliseconds(50),JobTimeout=whole?TimeSpan.FromMilliseconds(100):TimeSpan.FromSeconds(3)});
            await jobs.StartAsync(default);var accepted=jobs.Start("all",null);var timer=Stopwatch.StartNew();
            RadarJobView view;
            do {await Task.Delay(10);view=jobs.Get(accepted.JobId)!;}while(view.Status is "RUNNING" or "QUEUED"&&timer.Elapsed<TimeSpan.FromSeconds(3));
            check(view.Status==(whole?"TIMED_OUT":"COMPLETED"),whole?"whole job timeout reaches terminal state":"slow individual source completes with explicit missing data");
            if(!whole)check(view.Result!.Analysis[0].Score is null&&view.Result.Warnings.Length>0,"timeout never fabricates complete score");
            await jobs.StopAsync(default);
        }
    }
    private sealed class HangingHistory : IMarketRadarHistoryProvider
    {public async Task<RadarHistory> GetAsync(MarketQuote quote,CancellationToken ct) {await Task.Delay(Timeout.Infinite,ct);return new([],[],null,null);}}
    private sealed class UnusedRanking : ITaiwanMarketRankingProvider
    {public Task<MarketSnapshot> GetAsync(CancellationToken ct)=>throw new InvalidOperationException("Unexpected ranking call");}
}
