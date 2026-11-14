using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
public static class Phase4MCommands
{
    public static int Run(string[] args)
    {
        try {
            if(args.Length==6&&args[0]=="--phase4m-tick"&&args[5]=="--controlled") {
                var checks=HistoricalDatasetStore.Read<ReadinessCheck[]>(args[4]);
                Console.WriteLine(EtlProductionGate.Run(args[1],args[2],args[3],checks).GetAwaiter().GetResult().Status);return 0;
            }
            if(args.Length==8&&args[0]=="--phase4m-export") {
                var p=HistoricalDatasetStore.Read<Phase4IReport>(args[1]);var e=HistoricalDatasetStore.Read<Phase4JReport>(args[2]);
                var ops=Phase4LOperations.Create(HistoricalDatasetStore.Read<EtlPublication>(args[3]),HistoricalDatasetStore.Read<EtlOperationsSnapshot>(args[4]),e,p);
                AuthorityResolutionStore.CheckCode(args[6]);
                var queue=new AuthorityWorkbench(args[5]).Queue(e,p);var report=Phase4MOperations.Create(p,e,ops,queue,args[6]);
                Phase4MOperations.Validate(report,p,e);HistoricalDatasetStore.WriteNew(args[7],report);Console.WriteLine("EXPORTED_DISABLED_NOT_PROMOTED");return 0;
            }
            if(args.Length==7&&args[0]=="--phase4m-review") {
                // Roster is an operator-provisioned OS-protected file, never downloaded or supplied over HTTP.
                var roster=HistoricalDatasetStore.Read<ReviewActor[]>(args[4]);
                var actor=roster.SingleOrDefault(a=>a.Id==Environment.UserName)??throw new UnauthorizedAccessException();
                var p=HistoricalDatasetStore.Read<Phase4IReport>(args[1]);var e=HistoricalDatasetStore.Read<Phase4JReport>(args[2]);
                var result=new AuthorityWorkbench(args[3]).Append(e,p,actor,HistoricalDatasetStore.Read<ReviewCommand>(args[5]),DateTimeOffset.UtcNow);
                HistoricalDatasetStore.WriteNew(args[6],result);Console.WriteLine("REVIEW_RECORDED_NOT_DATASET_APPROVAL");return 0;
            }
            if(args.Length==4&&args[0]=="--phase4m-backup") {HistoricalDatasetStore.WriteNew(args[3],OperationalBackup.CopyAndVerify(args[1],args[2]));Console.WriteLine("BACKUP_BYTES_VERIFIED_REPLAY_REQUIRED");return 0;}
            throw new ArgumentException("--phase4m-export <phase4i> <phase4j> <etl> <scheduler> <review-root> <source-sha> <new-output> | --phase4m-review <phase4i> <phase4j> <root> <protected-roster> <command> <new-receipt> | --phase4m-backup <offline-root> <new-destination> <new-receipt>");
        }catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or System.Text.Json.JsonException){Console.Error.WriteLine("PHASE4M_BLOCKED: "+e.GetType().Name);return 1;}
    }
}
