using WorkJournal.Application.Research;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
public static class Phase4PCommands
{
    public static async Task<int> Run(string[] args)
    {
        try
        {
            if(args.Length==5&&args[0]=="--phase4p-export")
            {
                var parent=HistoricalDatasetStore.Read<Phase4OReport>(args[1]);var previous=HistoricalDatasetStore.Read<Phase4NReport>(args[2]);
                Phase4OOperations.Validate(parent,previous);AuthorityResolutionStore.CheckCode(args[3]);
                var report=Phase4POperations.Create(parent,args[3],DateTimeOffset.UtcNow);HistoricalDatasetStore.WriteNew(args[4],report);
                Console.WriteLine("PHASE4P_ZERO_REAL_COUNTS_BLOCKED");return 0;
            }
            if(args.Length==8&&args[0]=="--phase4p-acquisition")
            {
                var scope=HistoricalDatasetStore.Read<PilotScope>(args[2]);AuthorityResolutionStore.CheckCode(scope.Requests[0].SourceCodeSha);
                var auth=HistoricalDatasetStore.Read<PilotAuthorization>(args[3]);
                if(scope.Requests.Any(p=>p.Fixture))throw new InvalidOperationException("FIXTURE_ONLY_IN_TEST_HARNESS");
                PilotTrust Trust(){var t=HistoricalDatasetStore.Read<PilotTrust>(args[4]);var expected=Environment.GetEnvironmentVariable("WORKJOURNAL_PILOT_TRUST_DIGEST");
                    if(!PilotScopeRules.Digest(expected??"")||ResearchMath.Digest(t)!=expected)throw new InvalidOperationException("EXTERNALLY_PINNED_TRUST_REQUIRED");return t;}
                using var stop=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
                var result=await new RealAcquisitionWorker(args[1],Trust).Run(scope,auth,args[5],args[6],stop.Token);
                HistoricalDatasetStore.WriteNew(args[7],result);Console.WriteLine(result.Status);return result.Status=="BLOCKED"?2:0;
            }
            if(args.Length==12&&args[0]=="--phase4p-features")
            {
                var scope=HistoricalDatasetStore.Read<PilotScope>(args[6]);AuthorityResolutionStore.CheckCode(scope.Requests[0].SourceCodeSha);
                var auth=HistoricalDatasetStore.Read<PilotAuthorization>(args[7]);var trust=HistoricalDatasetStore.Read<PilotTrust>(args[8]);
                if(ResearchMath.Digest(trust)!=Environment.GetEnvironmentVariable("WORKJOURNAL_PILOT_TRUST_DIGEST"))throw new InvalidOperationException("EXTERNALLY_PINNED_TRUST_REQUIRED");
                var result=PilotFeatureOutcomeFactory.Prepare(new(args[1],args[2],args[3],args[4]),Guid.Parse(args[5]),scope,auth,trust,
                    HistoricalDatasetStore.Read<ResearchRequest>(args[9]),args[10],DateTimeOffset.UtcNow);
                HistoricalDatasetStore.WriteNew(args[11],result);Console.WriteLine(result.Status);return 0;
            }
            throw new ArgumentException("--phase4p-export <phase4o> <phase4n> <sha> <new-output> | --phase4p-acquisition <store> <scope> <authorization> <external-trust> <run-digest> <PLAN|DRY_RUN|ACQUIRE_ONCE|REPLAY|VALIDATE> <new-output> | --phase4p-features <approval-store> <evidence> <authority> <closure> <approval-id> <scope> <authorization> <trust> <research-template> <sha> <new-output>");
        }
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or OperationCanceledException)
        {Console.Error.WriteLine("PHASE4P_BLOCKED: "+e.GetType().Name);return 1;}
    }
}
