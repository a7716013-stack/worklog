using WorkJournal.Application.Research;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.HistoricalResearch;
public static class EtlCommands
{
    public static int Run(string[] args)
    {
        try
        {
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
