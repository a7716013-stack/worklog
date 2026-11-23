using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
public static class Phase4RCommands
{
    public static int Run(string[] args)
    {
        try {
            if(args.Length==6&&args[0]=="--phase4r-export"){
                AuthorityResolutionStore.CheckCode(args[4]);
                var parent=HistoricalDatasetStore.Read<Phase4QReport>(args[1]);
                Phase4QOperations.Validate(parent,HistoricalDatasetStore.Read<Phase4PReport>(args[2]),HistoricalDatasetStore.Read<Phase4IReport>(args[3]));
                HistoricalDatasetStore.WriteNew(args[5],Phase4ROperations.Create(parent,args[4],DateTimeOffset.UtcNow));
                Console.WriteLine("PHASE4R_REVIEW_ONLY_NO_ACQUISITION");return 0;
            }
            if(args.Length==3&&args[0]=="--phase4r-draft-scope"){
                AuthorityResolutionStore.CheckCode(args[1]);HistoricalDatasetStore.WriteNew(args[2],Phase4ROperations.DraftScope(args[1]));
                Console.WriteLine("PROPOSED_10_STOCKS_3_MONTHS_30_REQUESTS_90_ATTEMPTS_NOT_APPROVED");return 0;
            }
            throw new ArgumentException("--phase4r-export <phase4q> <phase4p> <phase4i> <compiled-sha> <new-output> | --phase4r-draft-scope <compiled-sha> <new-output>; review only, no acquisition");
        }catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException){Console.Error.WriteLine("PHASE4R_BLOCKED: "+e.GetType().Name);return 1;}
    }
}
