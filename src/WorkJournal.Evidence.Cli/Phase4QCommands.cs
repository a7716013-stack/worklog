using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
public static class Phase4QCommands
{
    public static int Run(string[] args)
    {
        try
        {
            if(args.Length==5&&args[0]=="--phase4q-export"){
                AuthorityResolutionStore.CheckCode(args[3]);
                var report=Phase4QOperations.Create(HistoricalDatasetStore.Read<Phase4PReport>(args[1]),HistoricalDatasetStore.Read<Phase4IReport>(args[2]),args[3],DateTimeOffset.UtcNow);
                HistoricalDatasetStore.WriteNew(args[4],report);Console.WriteLine("PHASE4Q_BLOCKED_NO_REAL_ACQUISITION");return 0;}
            if(args.Length==3&&args[0]=="--phase4q-draft-scope"){
                AuthorityResolutionStore.CheckCode(args[1]);HistoricalDatasetStore.WriteNew(args[2],Phase4QOperations.DraftScope(args[1]));
                Console.WriteLine("PROPOSED_ONLY_REQUIRES_HUMAN_CONFIRMATION_AND_NEW_DIGEST_AFTER_EDITS");return 0;}
            throw new ArgumentException("--phase4q-export <phase4p> <phase4i> <sha> <new-output> | --phase4q-draft-scope <sha> <new-output>; acquisition reuses --phase4p-acquisition with WORKJOURNAL_PILOT_EXECUTION_FILE and externally pinned trust");
        }
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {Console.Error.WriteLine("PHASE4Q_BLOCKED: "+e.GetType().Name);return 1;}
    }
}
