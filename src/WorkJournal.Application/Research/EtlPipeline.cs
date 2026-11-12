using System.Security.Cryptography;
using System.Text.Json;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

// Controlled immutable archive ingestion. Network acquisition remains Phase4JAcquisition's responsibility.
public sealed class EtlPipeline(string root)
{
    public static string Identity(ProviderRequest request,string sourceUrl,bool synthetic)=>ResearchMath.Digest(new{Request=request,SourceUrl=sourceUrl,Synthetic=synthetic});
    private string Folder(string id)
    {if(id.Length!=64||!id.All(Uri.IsHexDigit))throw new ArgumentException("Invalid ETL identity");return Path.Combine(root,id);}
    private static EtlJobReport Seal(EtlJobReport r)=>r with{Digest=ResearchMath.Digest(r with{Digest=""})};
    public EtlJobReport Execute(ProviderRequest request,SourceArchive raw,bool retry=false,CancellationToken ct=default)
    {
        var id=Identity(request,raw.SourceUri,raw.Synthetic);var folder=Folder(id);Directory.CreateDirectory(folder);
        using var gate=AcquisitionSafety.Lease(folder);
        var started=Directory.GetFiles(folder,"*.started.json").Length;
        var previous=Latest(id);
        if(previous is not null&&(!retry||previous.Status is "SUCCEEDED" or "QUARANTINED"))return Replay(id);
        if(started>=3)throw new InvalidOperationException("ETL_RETRY_LIMIT");
        var attempt=started+1;var now=DateTimeOffset.UtcNow;var stages=new List<EtlTransition>();
        void Stage(string state){var s=new EtlTransition(stages.Count+1,state,DateTimeOffset.UtcNow,stages.LastOrDefault()?.Digest??"","");stages.Add(s with{Digest=ResearchMath.Digest(s)});}
        var requestPath=Path.Combine(folder,"request.json");var rawPath=Path.Combine(folder,"raw-archive.json");
        if(!File.Exists(requestPath))HistoricalDatasetStore.WriteNew(requestPath,request);
        else if(ResearchMath.Digest(HistoricalDatasetStore.Read<ProviderRequest>(requestPath))!=ResearchMath.Digest(request))throw new InvalidDataException("IDEMPOTENCY_CONFLICT");
        HistoricalDatasetStore.WriteNew(Path.Combine(folder,$"{attempt:D2}.started.json"),new{JobId=id,Attempt=attempt,StartedAt=now});
        DataQualityReport? quality=null;EtlLineage? lineage=null;string status;string? error=null;int count=0,loaded=0;
        try
        {
            Stage("EXTRACTING");ct.ThrowIfCancellationRequested();
            if(raw.BodyBase64.Length>AcquisitionSafety.MaximumBytes*4/3+8)throw new InvalidDataException("ARTIFACT_TOO_LARGE");
            if(raw.SourceChecksum!=request.SourceVersion||Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(raw.BodyBase64)))!=request.SourceVersion)throw new InvalidDataException("RAW_DIGEST_MISMATCH");
            if(!File.Exists(rawPath))HistoricalDatasetStore.WriteNew(rawPath,raw);
            else raw=HistoricalDatasetStore.Read<SourceArchive>(rawPath); // Same identity reuses original capture metadata.
            Stage("EXTRACTED");ct.ThrowIfCancellationRequested();Stage("TRANSFORMING");
            var response=new OfficialDataProviderAdapter(request.Provider).Transform(request,raw);count=response.Facts.Length;
            ct.ThrowIfCancellationRequested();Stage("QUALITY");quality=EtlQuality.Evaluate(request,response);
            if(quality.Status=="QUARANTINED"){status="QUARANTINED";Stage(status);}
            else
            {
                Stage("LOADING");ct.ThrowIfCancellationRequested();
                lineage=Lineage(id,request,raw,response,quality);
                var batch=new EtlBatch(request,raw,response.Facts,quality,lineage,"");batch=batch with{Digest=ResearchMath.Digest(batch)};
                var path=Path.Combine(folder,"batch.json");
                if(!File.Exists(path))HistoricalDatasetStore.WriteNew(path,batch);
                else if(HistoricalDatasetStore.Read<EtlBatch>(path).Digest!=batch.Digest)throw new InvalidDataException("VERSIONED_LOAD_CONFLICT");
                loaded=response.Facts.Length;status="SUCCEEDED";Stage(status);
            }
        }
        catch(Exception e) when(e is OperationCanceledException or IOException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException or FormatException or OverflowException)
        {status=e is OperationCanceledException?"CANCELLED":"FAILED";error=ProviderErrorClassification.Classify(e);Stage(status);loaded=0;lineage=null;}
        var report=Seal(new(id,attempt,status,now,DateTimeOffset.UtcNow,request,raw.Synthetic,count,loaded,quality,lineage,stages.ToArray(),error,
            "REVIEW_REQUIRED","NOT_EVALUATED","NOT_APPROVED","NOT_RUN",""));
        HistoricalDatasetStore.WriteNew(Path.Combine(folder,$"{attempt:D2}.result.json"),report);return report;
    }
    private static EtlLineage Lineage(string id,ProviderRequest request,SourceArchive raw,ProviderResponse response,DataQualityReport quality)
    {
        var l=new EtlLineage(id,id,request.Provider,raw.SourceUri,raw.SourceChecksum,raw.ArchiveId,raw.RetrievedAt,request.SourceVersion,
            request.SchemaVersion,request.TransformCodeSha,response.ParserVersion,ResearchMath.Digest(response.Facts),quality.Digest,response.Facts.Select(f=>f.FactChecksum).ToArray(),"");
        return l with{Digest=ResearchMath.Digest(l)};
    }
    private EtlJobReport? Latest(string id)
    {
        var dir=Folder(id);if(!Directory.Exists(dir))return null;
        var path=Directory.GetFiles(dir,"*.result.json").Order(StringComparer.Ordinal).LastOrDefault();
        if(path is null)return null;var r=HistoricalDatasetStore.Read<EtlJobReport>(path);Validate(r);return r;
    }
    public EtlJobReport Replay(string id)
    {
        var r=Latest(id)??throw new InvalidOperationException("ETL_JOB_NOT_COMPLETE");var dir=Folder(id);
        if(r.JobId!=id||ResearchMath.Digest(r.Request)!=ResearchMath.Digest(HistoricalDatasetStore.Read<ProviderRequest>(Path.Combine(dir,"request.json"))))throw new InvalidDataException("ETL_REQUEST_CHANGED");
        if(r.Status is not ("SUCCEEDED" or "QUARANTINED"))return r;
        var raw=HistoricalDatasetStore.Read<SourceArchive>(Path.Combine(dir,"raw-archive.json"));
        var response=new OfficialDataProviderAdapter(r.Request.Provider).Transform(r.Request,raw);
        var quality=EtlQuality.Evaluate(r.Request,response);
        if(Identity(r.Request,raw.SourceUri,raw.Synthetic)!=id||quality.Digest!=r.Quality!.Digest||r.Synthetic!=raw.Synthetic)throw new InvalidDataException("QUALITY_REPLAY_MISMATCH");
        if(r.Status=="SUCCEEDED")
        {
            var b=HistoricalDatasetStore.Read<EtlBatch>(Path.Combine(dir,"batch.json"));var lineage=Lineage(id,r.Request,raw,response,quality);
            if(b.Digest!=ResearchMath.Digest(b with{Digest=""})||ResearchMath.Digest(b.Facts)!=ResearchMath.Digest(response.Facts)||
                ResearchMath.Digest(b.Raw)!=ResearchMath.Digest(raw)||b.Lineage.Digest!=lineage.Digest||r.Lineage?.Digest!=lineage.Digest)throw new InvalidDataException("TRANSFORM_LINEAGE_REPLAY_MISMATCH");
        }
        return r;
    }
    public static void Validate(EtlJobReport r)
    {
        if(r.Digest!=ResearchMath.Digest(r with{Digest=""})||r.Attempt is <1 or >3||r.Status is not ("SUCCEEDED" or "QUARANTINED" or "FAILED" or "CANCELLED")||
            r.AuthorityStatus!="REVIEW_REQUIRED"||r.PitStatus!="NOT_EVALUATED"||r.DatasetApproval!="NOT_APPROVED"||r.ResearchStatus!="NOT_RUN"||
            r.Status=="SUCCEEDED"&&(r.Quality is null||r.Lineage is null||r.LoadedFacts!=r.FactsCount||r.Quality.Quarantine.Length!=0)||
            r.Status!="SUCCEEDED"&&r.LoadedFacts!=0||r.Stages.Length==0||r.Stages[^1].State!=r.Status)throw new InvalidDataException("ETL_REPORT_INVALID");
        string[] sequence=["EXTRACTING","EXTRACTED","TRANSFORMING","QUALITY","LOADING","SUCCEEDED"];
        for(var i=0;i<r.Stages.Length;i++){
            var s=r.Stages[i];var terminal=i==r.Stages.Length-1;
            var valid=terminal&&s.State is "FAILED" or "CANCELLED" || terminal&&s.State=="QUARANTINED"&&i==4 || i<sequence.Length&&s.State==sequence[i];
            if(!valid||s.Sequence!=i+1||s.PreviousDigest!=(i==0?"":r.Stages[i-1].Digest)||s.Digest!=ResearchMath.Digest(s with{Digest=""})||s.At<r.StartedAt||s.At>r.CompletedAt||i>0&&s.At<r.Stages[i-1].At)throw new InvalidDataException("ETL_STATE_CHAIN_CHANGED");
        }
    }
    public EtlPublication Export()
    {
        var jobs=Directory.Exists(root)?Directory.GetDirectories(root).Where(p=>Path.GetFileName(p).Length==64).Select(p=>Replay(Path.GetFileName(p))).OrderBy(j=>j.JobId,StringComparer.Ordinal).ToArray():[];
        var r=new EtlPublication("EtlPublication-v1",DateTimeOffset.UtcNow,OfficialDataProviderAdapter.Catalog(),jobs,"Raw/transform/quality replay at controlled export; no live provider health or authority approval","");
        return r with{Digest=ResearchMath.Digest(r)};
    }
    public static void ValidatePublication(EtlPublication r)
    {
        if(r.SchemaVersion!="EtlPublication-v1"||r.Digest!=ResearchMath.Digest(r with{Digest=""})||ResearchMath.Digest(r.Providers)!=ResearchMath.Digest(OfficialDataProviderAdapter.Catalog())||r.Jobs.Select(j=>j.JobId).Distinct().Count()!=r.Jobs.Length)throw new InvalidDataException("ETL_PUBLICATION_INVALID");
        foreach(var j in r.Jobs)Validate(j);
    }
}
