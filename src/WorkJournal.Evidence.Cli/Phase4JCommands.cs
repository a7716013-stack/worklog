using WorkJournal.Application.Research;
using WorkJournal.Web.Models.HistoricalResearch.Resolution;
using WorkJournal.Web.Services.HistoricalResearch;
public static class Phase4JCommands
{
    public static async Task<int> Run(string[] args)
    {
        try
        {
            if(args.Length==3&&args[0]=="--phase4j-replay")
            {var p=new Phase4IReadService(args[1]).Read();Phase4JProjection.Validate(HistoricalDatasetStore.Read<Phase4JReport>(args[2]),p);Console.WriteLine("PHASE4J_REPORT_INTEGRITY_MATCH");return 0;}
            if(args.Length==9&&args[0]=="--phase4j-export")
            {
                AuthorityResolutionStore.CheckCode(args[6]);var at=DateTimeOffset.UtcNow;
                var request=HistoricalDatasetStore.Read<TrendResolutionRequest>(args[1]);
                var p=new Phase4IWorkflow(args[2],args[3],args[4],args[5]).Export(request,args[6],at);
                var r=new Phase4JWorkflow(args[2],args[3],args[4],args[5]).Project(p,request,at);
                HistoricalDatasetStore.WriteNew(args[7],p);HistoricalDatasetStore.WriteNew(args[8],r);Console.WriteLine("PHASE4J_EXPORTED_NO_APPROVAL");return 0;
            }
            if(args.Length==7&&args[0]=="--phase4j-invalidate")
            {
                var p=new Phase4IReadService(args[1]).Read();var r=HistoricalDatasetStore.Read<Phase4JReport>(args[2]);Phase4JProjection.Validate(r,p);
                var impact=Phase4JProjection.Invalidate(r,args[4],args[5],args[6]).Single();
                var dir=Path.Combine(args[3],"supply-chain");Directory.CreateDirectory(dir);
                HistoricalDatasetStore.WriteNew(Path.Combine(dir,Guid.NewGuid()+".impact.json"),impact);
                Console.WriteLine("REEVALUATION_REQUIRED_REEXPORT_BEFORE_PUBLICATION");return 0;
            }
            if(args.Length==9&&args[0]=="--phase4j-acquire")
            {
                var p=new Phase4IReadService(args[1]).Read();var r=HistoricalDatasetStore.Read<Phase4JReport>(args[2]);
                var result=await new Phase4JAcquisition(args[3],args[4]).Execute(r,p,args[5],HistoricalDatasetStore.Read<AuthorityResolutionTask>(args[6]),args[7]);
                HistoricalDatasetStore.WriteNew(args[8],result);Console.WriteLine("CAPTURE_ONLY_REVIEW_REQUIRED");return 0;
            }
            throw new ArgumentException("--phase4j-export <request> <resolution> <evidence> <authority> <closure> <compiled-sha> <new-parent> <new-report> | --phase4j-replay <parent> <report> | --phase4j-acquire <parent> <report> <resolution> <evidence> <task-id> <legacy-task> <operator> <new-output>");
        }
        catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {Console.Error.WriteLine("PHASE4J_BLOCKED: "+e.Message);return 1;}
    }
}
