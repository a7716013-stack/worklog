using WorkJournal.Application.Research;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.HistoricalResearch;
public static class EtlCommands
{
    public static int Run(string[] args)
    {
        try
        {
            if(args.Length==6&&args[0]=="--etl-operations-report")
            {var r=Phase4LOperations.Create(HistoricalDatasetStore.Read<EtlPublication>(args[1]),HistoricalDatasetStore.Read<EtlOperationsSnapshot>(args[2]),HistoricalDatasetStore.Read<Phase4JReport>(args[3]),HistoricalDatasetStore.Read<Phase4IReport>(args[4]));HistoricalDatasetStore.WriteNew(args[5],r);Console.WriteLine($"OPERATIONS_EXPORT {r.Closure.Length} tasks; production scheduler disabled");return 0;}
            if(args.Length==5&&args[0]=="--etl-schedule-register")
            {var r=new EtlScheduler(args[1]).Register(HistoricalDatasetStore.Read<ScheduledJobDefinition>(args[2]),HistoricalDatasetStore.Read<SourceArchive>(args[3]));HistoricalDatasetStore.WriteNew(args[4],r);Console.WriteLine(r.Execution.Status);return 0;}
            if(args.Length==4&&args[0]=="--etl-schedule-tick"&&args[3]=="--controlled")
            {var r=new EtlScheduler(args[1]).RunOnce(args[2]).GetAwaiter().GetResult();Console.WriteLine(r?.Status??"NO_DISPATCH");return r?.Status=="FAILED"?1:0;}
            if(args.Length==3&&args[0]=="--etl-schedule-cancel")
            {Console.WriteLine(new EtlScheduler(args[1]).Cancel(args[2]).Status);return 0;}
            if(args.Length==3&&args[0]=="--etl-operations-export")
            {var r=Phase4LOperations.Snapshot(new EtlScheduler(args[1]).Snapshot(),DateTimeOffset.UtcNow);Phase4LOperations.Validate(r);HistoricalDatasetStore.WriteNew(args[2],r);Console.WriteLine("CONTROLLED_EXPORT_SNAPSHOT_NOT_LIVE_HEALTH");return 0;}
            if(args.Length is 5 or 6&&args[0]=="--etl-run")
            {
                var request=HistoricalDatasetStore.Read<ProviderRequest>(args[1]);AuthorityResolutionStore.CheckCode(request.TransformCodeSha);
                var raw=HistoricalDatasetStore.Read<SourceArchive>(args[2]);
                var report=new EtlPipeline(args[3]).Execute(request,raw,args.Length==6&&args[5]=="--retry");
                EtlPipeline.Validate(report);HistoricalDatasetStore.WriteNew(args[4],report);Console.WriteLine(report.Status);return report.Status=="FAILED"?1:0;
            }
            if(args.Length==3&&args[0]=="--etl-replay"){Console.WriteLine(new EtlPipeline(args[1]).Replay(args[2]).Status+" REPLAY_MATCH");return 0;}
            if(args.Length==4&&args[0]=="--etl-export"){AcquisitionSafety.RequireCurrentSources(args[2]);var r=new EtlPipeline(args[1]).Export();EtlPipeline.ValidatePublication(r);HistoricalDatasetStore.WriteNew(args[3],r);Console.WriteLine($"ETL_EXPORT {r.Jobs.Length} jobs; no authority approval");return 0;}
            throw new ArgumentException("--etl-run <provider-request> <existing-raw-archive> <etl-root> <new-report> [--retry] | --etl-replay <etl-root> <job-id> | --etl-export <etl-root> <authority-resolution-root> <new-publication>. Network acquisition uses existing --phase4j-acquire; no arbitrary URL ETL download.");
        }
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {Console.Error.WriteLine("ETL_BLOCKED: "+e.GetType().Name);return 1;}
    }
}
