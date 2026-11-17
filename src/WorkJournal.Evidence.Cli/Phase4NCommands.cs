using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
public static class Phase4NCommands
{
    public static int Run(string[] args)
    {
        try {
            if(args.Length!=6)throw new ArgumentException("--phase4n-export <phase4i> <phase4j> <phase4m> <source-sha> <new-output>");
            AuthorityResolutionStore.CheckCode(args[4]);
            var p=HistoricalDatasetStore.Read<Phase4IReport>(args[1]);var e=HistoricalDatasetStore.Read<Phase4JReport>(args[2]);var m=HistoricalDatasetStore.Read<Phase4MReport>(args[3]);
            var report=Phase4NOperations.Create(p,e,m,args[4],DateTimeOffset.UtcNow);Phase4NOperations.Validate(report,p,e,m);
            HistoricalDatasetStore.WriteNew(args[5],report);Console.WriteLine("EXPORTED_DISABLED_NOT_PROMOTED");return 0;
        }catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException){Console.Error.WriteLine("PHASE4N_BLOCKED: "+e.GetType().Name);return 1;}
    }
}
