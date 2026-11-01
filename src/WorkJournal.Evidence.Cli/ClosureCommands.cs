using System.Text.Json;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;

internal static class ClosureCommands
{
    public static int Run(string[] args)
    {
        try
        {
            if(args.Length==3&&args[0]=="--closure-subject")
            {
                var request=HistoricalDatasetStore.Read<RealDatasetSubmission>(args[1]);
                Console.WriteLine(AuthorityClosureStore.Subject(request.Source.Dataset,Enum.Parse<ClosureDomain>(args[2])));return 0;
            }
            if(args.Length==4&&args[0]=="--closure-open")
            {
                var r=new AuthorityClosureStore(args[2],args[3]).Open(HistoricalDatasetStore.Read<AuthorityClosureCase>(args[1]));
                Console.WriteLine($"{r.Case.CurrentStatus} {r.Digest}");return 0;
            }
            if(args.Length==4&&args[0]=="--closure-review")
            {
                var r=new AuthorityClosureStore(args[2],args[3]).Review(HistoricalDatasetStore.Read<ClosureReviewRequest>(args[1]));
                Console.WriteLine($"{r.Case.CurrentStatus} {r.Digest}");return 0;
            }
            if(args.Length==5&&args[0]=="--closure-export")
            {
                HistoricalDatasetStore.WriteNew(args[4],new AuthorityClosureStore(args[1],args[2]).Read(Guid.Parse(args[3])));return 0;
            }
            if(args.Length==5&&args[0]=="--closure-evaluate")
            {
                var r=new AuthorityClosureStore(args[2],args[3]).Evaluate(HistoricalDatasetStore.Read<ComponentClosureRequest>(args[1]));
                HistoricalDatasetStore.WriteNew(args[4],r);Console.WriteLine($"{r.Status} {r.ReportDigest}");return 0;
            }
            if(args.Length==7&&args[0]=="--closure-baseline")
            {
                var request=HistoricalDatasetStore.Read<ComponentClosureRequest>(args[1]);
                var closure=new AuthorityClosureStore(args[3],args[4]).Evaluate(request);
                if(closure.Status!="CLOSURE_READY_REQUIRES_DATASET_APPROVAL")throw new InvalidOperationException("BLOCKED_BY_AUTHORITY_OR_DATA: closure claims incomplete.");
                var path=new RealBaselineStore(args[6]).Execute(request.Dataset,HistoricalDatasetStore.Read<ResearchRequest>(args[2]),args[4],args[5],TimeProvider.System,closure,args[3]);
                Console.WriteLine(path);return 0;
            }
            Console.Error.WriteLine("Usage: --closure-open <case.json> <closure-store> <evidence-store> | --closure-review <review.json> <closure-store> <evidence-store> | --closure-export <closure-store> <evidence-store> <case-guid> <new-file> | --closure-evaluate <request.json> <closure-store> <evidence-store> <new-file> | --closure-baseline <request.json> <research-template.json> <closure-store> <evidence-store> <authority-store> <baseline-store>");return 2;
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or IOException or InvalidDataException or JsonException or FormatException)
        {Console.Error.WriteLine(ex.Message);return 1;}
    }
}
