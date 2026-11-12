using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WorkJournal.Application.Research;
using WorkJournal.Api.Services;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
public static class EtlChecks
{
    public static async Task Run(HttpClient http,Action<bool,string> check)
    {
        string[] paths=["/api/v1/data/providers","/api/v1/etl/jobs","/api/v1/etl/jobs/missing","/api/v1/etl/jobs/missing/quality","/api/v1/etl/batches/missing/lineage","/api/v1/etl/health"];
        http.DefaultRequestHeaders.Remove("X-Test-User");
        foreach(var path in paths)check((await http.GetAsync(path)).StatusCode==HttpStatusCode.Unauthorized,"ETL member authentication "+path);
        http.DefaultRequestHeaders.Add("X-Test-User","alice");
        foreach(var path in new[]{paths[0],paths[1],paths[5]})check((await http.GetAsync(path)).IsSuccessStatusCode,"ETL read contract "+path);
        foreach(var path in paths.Skip(2).Take(3))check((await http.GetAsync(path)).StatusCode==HttpStatusCode.NotFound,"ETL unknown resource");
        foreach(var query in new[]{"page=0","pageSize=201","provider=evil","status=APPROVED","sort=sql"})check((await http.GetAsync(paths[1]+"?"+query)).StatusCode==HttpStatusCode.BadRequest,"ETL query whitelist "+query);
        check((await http.PostAsync(paths[1],new StringContent("{}"))).StatusCode==HttpStatusCode.MethodNotAllowed,"ETL execution not exposed publicly");
        var published=new EtlReadAdapter().Read();
        foreach(var j in published.Jobs)foreach(var path in new[]{"/api/v1/etl/jobs/"+j.JobId,"/api/v1/etl/jobs/"+j.JobId+"/quality","/api/v1/etl/batches/"+j.JobId+"/lineage"})check((await http.GetAsync(path)).IsSuccessStatusCode,"ETL published details");
        var root=Path.Combine(Path.GetTempPath(),"worklog-etl-test-"+Guid.NewGuid());
        var pipeline=new EtlPipeline(root);var now=DateTimeOffset.UtcNow;
        SourceArchive Raw(string[][] rows,string provider="TWSE") {
            var fields=provider=="TWSE"?new[]{"日期","成交股數","成交金額","開盤價","最高價","最低價","收盤價","漲跌價差"}:new[]{"日期","成交股數","成交金額","開盤","最高","最低","收盤","漲跌"};
            var bytes=JsonSerializer.SerializeToUtf8Bytes(new{stat="OK",date="20260901",fields,data=rows});
            return new(Guid.NewGuid().ToString(),provider,HistoricalDatasetType.Price,provider=="TWSE"?"https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY?date=20260901&stockNo=2330&response=json":"https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock?date=2026/09/01&code=2330",now,now,Convert.ToBase64String(bytes),Convert.ToHexString(SHA256.HashData(bytes)),provider+"-capture-v1",true);
        }
        ProviderRequest Request(SourceArchive a)=>new(a.Provider,"Price","2330",new(2026,9,1),new(2026,9,1),new(2026,9,30),a.SourceChecksum,new('a',40));
        void Reject(Action action,string name){try{action();}catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or InvalidOperationException){check(true,name);return;}throw new Exception("Expected rejection "+name);}
        string[] row=["115/09/01","1000","100000","100","110","90","105","5"];
        var raw=Raw([row]);var request=Request(raw);var result=pipeline.Execute(request,raw);
        check(result.Status=="SUCCEEDED"&&result.LoadedFacts==1,"ETL fixture transform and versioned load");
        check(pipeline.Execute(request,raw).Digest==result.Digest&&pipeline.Replay(result.JobId).Digest==result.Digest,"ETL idempotence and replay");
        check(EtlPipeline.Identity(request,raw.SourceUri,true)!=EtlPipeline.Identity(request,raw.SourceUri,false),"ETL real and fixture identities distinct");
        var batch=HistoricalDatasetStore.Read<EtlBatch>(Path.Combine(root,result.JobId,"batch.json"));
        check(batch.Facts.All(f=>f.AvailableAt is null&&f.Confidence==AvailabilityConfidence.Unknown)&&result.DatasetApproval=="NOT_APPROVED"&&result.ResearchStatus=="NOT_RUN","ETL capture never grants PIT approval or research");
        var duplicate=Raw([row,row]);var quarantine=pipeline.Execute(Request(duplicate),duplicate);
        check(quarantine.Status=="QUARANTINED"&&quarantine.LoadedFacts==0&&quarantine.Quality!.Quarantine.Length==2&&!File.Exists(Path.Combine(root,quarantine.JobId,"batch.json")),"ETL duplicate conflicts quarantine all rows");
        var missing=Raw([["115/09/01","1000","100000","--","110","90","105","5"]]);
        check(pipeline.Execute(Request(missing),missing).Status=="QUARANTINED","ETL missing price not zero");
        var noData=Raw([]);check(pipeline.Execute(Request(noData),noData).Status=="QUARANTINED","ETL empty response not successful historical proof");
        var otc=Raw([row],"TPEx");var transformed=new OfficialDataProviderAdapter("TPEx").Transform(Request(otc),otc);
        check(transformed.Facts[0].Value.Volume is null&&EtlQuality.Evaluate(Request(otc),transformed).Checks.Single(c=>c.Check=="Units").Status=="UNKNOWN","TPEx rounded volume remains unknown exact shares");
        Reject(()=>new OfficialDataProviderAdapter("TWSE").Transform(request,raw with{SourceUri="https://127.0.0.1/"}),"ETL unsafe source URL rejected");
        Reject(()=>new OfficialDataProviderAdapter("TWSE").Transform(request with{Instrument="9999"},raw),"ETL instrument binding");
        Reject(()=>new OfficialDataProviderAdapter("TWSE").Transform(request with{Date=new(2026,9,2)},raw),"ETL source request date binding");
        Reject(()=>new OfficialDataProviderAdapter("TWSE").Transform(request,raw with{ParserVersion="new-schema"}),"ETL schema drift fails closed");
        var bad=raw with{BodyBase64=Convert.ToBase64String("tamper"u8.ToArray())};
        var failed=new EtlPipeline(Path.Combine(root,"failure"));
        check(failed.Execute(request,bad).Status=="FAILED"&&failed.Execute(request,bad,true).Attempt==2&&failed.Execute(request,bad,true).Attempt==3,"ETL persisted bounded retries");
        Reject(()=>failed.Execute(request,bad,true),"ETL retry ceiling");
        var cancelled=new EtlPipeline(Path.Combine(root,"cancel")).Execute(request,raw,ct:new CancellationToken(true));
        check(cancelled.Status=="CANCELLED"&&cancelled.LoadedFacts==0,"ETL cancellation never loads");
        using(var lease=AcquisitionSafety.Lease(Path.Combine(root,result.JobId)))Reject(()=>pipeline.Execute(request,raw),"ETL per-job concurrent exclusion");
        File.WriteAllText(Path.Combine(root,result.JobId,"raw-archive.json"),JsonSerializer.Serialize(bad));
        Reject(()=>pipeline.Replay(result.JobId),"ETL raw tampering blocks replay");
        Reject(()=>pipeline.Export(),"ETL corrupted lineage blocks publication");
        var altered=result with{Stages=result.Stages.Select((s,i)=>i==0?s with{State="SUCCEEDED"}:s).ToArray()};
        Reject(()=>EtlPipeline.Validate(altered with{Digest=ResearchMath.Digest(altered with{Digest=""})}),"ETL invalid transition cannot be resealed into validity");
    }
}
