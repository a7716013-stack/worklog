using WorkJournal.Application.Research;
using WorkJournal.Web.Models.HistoricalResearch.Resolution;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;
public static class Phase4ICommands
{
    public static int Run(string[] args)
    {
        try
        {
            if(args.Length==2&&args[0]=="--phase4i-replay"){new Phase4IReadService(args[1]).Read();Console.WriteLine("PHASE4I_REPORT_INTEGRITY_MATCH");return 0;}
            if(args.Length is 8 or 10)
            {
                AuthorityResolutionStore.CheckCode(args[6]);
                var workflow=new Phase4IWorkflow(args[2],args[3],args[4],args[5]);
                var request=HistoricalDatasetStore.Read<TrendResolutionRequest>(args[1]);
                Phase4IReport report;
                if(args.Length==8&&args[0]=="--phase4i-export")report=workflow.Export(request,args[6],DateTimeOffset.UtcNow);
                else if(args.Length==10&&args[0]=="--phase4i-run")report=workflow.Run(request,Guid.Parse(args[7]),HistoricalDatasetStore.Read<ResearchRequest>(args[8]),args[6],DateTimeOffset.UtcNow);
                else throw new ArgumentException("Unsupported Phase4I command");
                Phase4IProjection.Validate(report);HistoricalDatasetStore.WriteNew(args[^1],report);Console.WriteLine(report.Trend.Status);return 0;
            }
            throw new ArgumentException("--phase4i-export <request> <resolution> <evidence> <authority> <closure> <compiled-sha> <new-report> | --phase4i-run <same six inputs> <approval-id> <template> <new-report> | --phase4i-replay <report>");
        }
        catch(Exception e) when(e is IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or FormatException)
        {Console.Error.WriteLine("PHASE4I_BLOCKED: "+e.Message);return 1;}
    }
}
