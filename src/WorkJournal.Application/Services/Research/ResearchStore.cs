using System.Text;
using System.Text.Json;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;
namespace WorkJournal.Web.Services.Research;

// Durable local research repository. Final runs are published atomically and never overwritten.
// No production DB migration and no writable unauthenticated HTTP endpoint.
public sealed class ResearchStore(string root)
{
    private readonly string root=Path.GetFullPath(root);
    private static readonly string[] Files=["research-run.json","research-metrics.json","evidence-distributions.json","quantiles.json","correlations.json","ablation.json","oos-results.json","research-observations.json","promotion-report.json","development-scorecards.json","request.json","report.json"];
    public string Execute(ResearchRequest request,ApprovedResearchAuthorization? authorization=null)
    {
        Directory.CreateDirectory(root);
        var final=Path.Combine(root,request.ResearchRunId.ToString("N"));
        if(Directory.Exists(final))throw new IOException("Finalized research run already exists.");
        var staging=Path.Combine(root,".pending-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        var state=ResearchRunStatus.Created;var lifecycle=new List<string>{state.ToString()};
        void Move(ResearchRunStatus next){state=ResearchProtocolRules.Transition(state,next,false);lifecycle.Add(state.ToString());}
        try
        {
            if(request.Snapshots.Any(s=>!s.Evidence.Synthetic)&&authorization is null)
                throw new InvalidOperationException("DATASET_APPROVAL_REQUIRED: new real research must enter through the reviewed authority archive.");
            if(authorization is not null)
            {
                authorization.Validate(request);
                Write("authority-approval.json",authorization.Archive);
            }
            // Preserve decimal scale in the source request because the locked 3.0.4 parameter
            // checksum uses that representation. Research digests themselves remain canonical.
            using(var raw=new FileStream(Path.Combine(staging,"request.json"),FileMode.CreateNew,FileAccess.Write))
            {JsonSerializer.Serialize(raw,ResearchEngine.CanonicalRequest(request));raw.Flush(true);}
            Move(ResearchRunStatus.Prepared);Move(ResearchRunStatus.Running);
            var report=new ResearchEngine().Run(request);
            Move(report.Status);
            Write("report.json",report);
            Write("research-run.json",new{request.ResearchRunId,request.CreatedAt,Status=state,Finalized=true,Lifecycle=lifecycle,request.CandidateVersion,request.ModelVersion,request.FeatureVersion,
                request.Protocol,SnapshotIdentities=request.Snapshots.Select(s=>s.Data.Identity).ToArray(),report.ResearchInputDigest,report.ResearchOutputDigest});
            Write("research-metrics.json",report.Metrics);
            Write("evidence-distributions.json",report.Metrics.Select(m=>new{m.Fold,m.Partition,m.Variable,m.Horizon,m.Distribution}).ToArray());
            Write("quantiles.json",report.Metrics.Select(m=>new{m.Fold,m.Partition,m.Variable,m.Horizon,m.Quantiles,m.TopMinusBottomSpread,m.AdjacentMonotonicity}).ToArray());
            Write("correlations.json",report.Correlations);
            Write("ablation.json",report.Metrics.Where(m=>m.Variable.StartsWith("Ablation.")).ToArray());
            Write("oos-results.json",report.Metrics.Where(m=>m.Partition==ResearchPartition.Oos).ToArray());
            Write("research-observations.json",report.Observations);
            Write("promotion-report.json",new{report.PromotionStatus,report.ModelStatus,report.ComponentStatus,report.Gaps,report.GroupMetrics});
            Write("development-scorecards.json",request.Protocol.Folds.OrderBy(f=>f.Oos.Start).Select(f=>ResearchEngine.Scorecard(request,report.Metrics,f.Code)).ToArray());
            WriteManifest();Directory.Move(staging,final);return final;
        }
        catch(Exception e)
        {
            // Preserve failed execution evidence; never mislabel it as Completed.
            if(state is ResearchRunStatus.Created or ResearchRunStatus.Prepared or ResearchRunStatus.Running)Move(ResearchRunStatus.Failed);
            if(!File.Exists(Path.Combine(staging,"failure.json")))Write("failure.json",new{request.ResearchRunId,Status=ResearchRunStatus.Failed,Lifecycle=lifecycle,Error=e.GetType().Name,Reason=e.Message});
            throw;
        }
        void Write<T>(string name,T value){using var stream=new FileStream(Path.Combine(staging,name),FileMode.CreateNew,FileAccess.Write);var bytes=Encoding.UTF8.GetBytes(ResearchMath.Canonical(value));stream.Write(bytes);stream.Flush(true);}
        void WriteManifest()=>Write("manifest.json",(authorization is null?Files:Files.Append("authority-approval.json")).ToDictionary(name=>name,name=>ResearchMath.Digest(File.ReadAllText(Path.Combine(staging,name)))));
    }
    public ResearchReport Replay(Guid runId)
    {
        var folder=Path.Combine(root,runId.ToString("N"));
        var manifest=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(folder,"manifest.json")))??throw new InvalidDataException("Missing manifest.");
        var expected=manifest.ContainsKey("authority-approval.json")?Files.Append("authority-approval.json").ToArray():Files;
        if(manifest.Count!=expected.Length || expected.Any(name=>!manifest.ContainsKey(name)||ResearchMath.Digest(File.ReadAllText(Path.Combine(folder,name)))!=manifest[name]))throw new InvalidDataException("Research artifacts were modified.");
        var request=JsonSerializer.Deserialize<ResearchRequest>(File.ReadAllText(Path.Combine(folder,"request.json")))??throw new InvalidDataException("Missing request.");
        if(request.ResearchRunId!=runId)throw new InvalidDataException("Run identity mismatch.");
        if(manifest.ContainsKey("authority-approval.json"))
            new ApprovedResearchAuthorization(HistoricalDatasetStore.Read<AuthorityArchive>(Path.Combine(folder,"authority-approval.json"))).Validate(request);
        var actual=new ResearchEngine().Run(request);
        if(ResearchMath.Canonical(actual)!=File.ReadAllText(Path.Combine(folder,"report.json")))throw new InvalidDataException("Research replay mismatch.");
        return actual;
    }
}
