using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
public static class Phase4OCommands
{
    public static int Run(string[] args)
    {
        try{
            if(args.Length==7&&args[0]=="--phase4o-export"){
                var p=HistoricalDatasetStore.Read<Phase4IReport>(args[1]);var e=HistoricalDatasetStore.Read<Phase4JReport>(args[2]);
                var m=HistoricalDatasetStore.Read<Phase4MReport>(args[3]);var n=HistoricalDatasetStore.Read<Phase4NReport>(args[4]);
                Phase4NOperations.Validate(n,p,e,m);AuthorityResolutionStore.CheckCode(args[5]);
                var report=Phase4OOperations.Create(n,args[5],DateTimeOffset.UtcNow);Phase4OOperations.Validate(report,n);HistoricalDatasetStore.WriteNew(args[6],report);
                Console.WriteLine("EXPORTED_ZERO_NEW_REAL_DATA_DISABLED");return 0;
            }
            if(args.Length==5&&args[0]=="--phase4o-fixture-capture"){
                var plan=HistoricalDatasetStore.Read<CapturePlan>(args[2]);if(!plan.Fixture)throw new InvalidOperationException("REAL_ACQUISITION_NOT_AUTHORIZED");
                AuthorityResolutionStore.CheckCode(plan.SourceCodeSha);
                var input=HistoricalDatasetStore.Read<CaptureInput>(args[3]);var store=new ForwardCaptureStore(args[1]);store.Append(plan,input,[],new HashSet<string>(),DateTimeOffset.UtcNow);
                HistoricalDatasetStore.WriteNew(args[4],store.Replay(plan));Console.WriteLine("FIXTURE_ARCHIVED_NO_NETWORK_NO_APPROVAL");return 0;
            }
            if(args.Length==4&&args[0]=="--phase4o-replay"){
                HistoricalDatasetStore.WriteNew(args[3],new ForwardCaptureStore(args[1]).Replay(HistoricalDatasetStore.Read<CapturePlan>(args[2])));Console.WriteLine("CAPTURE_REPLAY_VERIFIED_NOT_PIT_APPROVED");return 0;
            }
            if(args.Length==6&&args[0]=="--phase4o-quality"){
                var result=new ForwardCaptureStore(args[1]).Assess(HistoricalDatasetStore.Read<CapturePlan>(args[2]),args[3],HistoricalDatasetStore.Read<CaptureQualityContext>(args[4]));
                HistoricalDatasetStore.WriteNew(args[5],result);Console.WriteLine(result.Quality.Status+"_NOT_PIT_APPROVAL");return 0;
            }
            throw new ArgumentException("--phase4o-export <phase4i> <phase4j> <phase4m> <phase4n> <source-sha> <new-output> | --phase4o-fixture-capture <root> <plan> <input> <new-report> | --phase4o-replay <root> <plan> <new-report>");
        }catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or FormatException or System.Text.Json.JsonException){Console.Error.WriteLine("PHASE4O_BLOCKED: "+e.GetType().Name);return 1;}
    }
}
