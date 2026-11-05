using WorkJournal.Application.Research;
using WorkJournal.Domain.Research;
using WorkJournal.Web.Models.HistoricalResearch.Resolution;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;
public static class ValidationCommands
{
    public static int Run(string[] args)
    {
        try
        {
            if(args.Length==2&&args[0]=="--validation-replay") {new EvidenceValidationReadService(args[1]).Read();Console.WriteLine("VALIDATION_REPORT_REPLAY_MATCH");return 0;}
            if(args.Length>=8)
            {
                var request=HistoricalDatasetStore.Read<TrendResolutionRequest>(args[1]);
                var workflow=new EvidenceValidationWorkflow(args[2],args[3],args[4],args[5]);
                if(args[0]=="--validation-export"&&args.Length==8)
                {HistoricalDatasetStore.WriteNew(args[7],workflow.Evaluate(request,args[6],DateTimeOffset.UtcNow));return 0;}
                if(args[0]=="--validation-diagnose"&&args.Length==10)
                {HistoricalDatasetStore.WriteNew(args[9],workflow.Diagnose(request,Guid.Parse(args[7]),HistoricalDatasetStore.Read<V3EvidenceCandidateV1ParameterSet>(args[8]),args[6],DateTimeOffset.UtcNow));return 0;}
                if(args[0]=="--validation-baseline"&&args.Length==9)
                {Console.WriteLine(workflow.RunBaseline(request,Guid.Parse(args[7]),HistoricalDatasetStore.Read<ResearchRequest>(args[8]),args[6],DateTimeOffset.UtcNow));return 0;}
                if(args[0]=="--validation-hypothesis"&&args.Length==10)
                {HistoricalDatasetStore.WriteNew(args[9],workflow.Propose(Guid.Parse(args[7]),HistoricalDatasetStore.Read<CandidateHypothesis>(args[8])));return 0;}
            }
            throw new ArgumentException("--validation-export <request> <resolution> <evidence> <authority> <closure> <source-code-sha> <new-report> | --validation-diagnose <same six inputs> <approval-id> <parameters> <new-report> | --validation-baseline <same six inputs> <approval-id> <template> | --validation-hypothesis <same six inputs> <run-id> <hypothesis> <new-receipt> | --validation-replay <report>");
        }
        catch(Exception e) when(e is IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or FormatException)
        {Console.Error.WriteLine("VALIDATION_BLOCKED: "+e.Message);return 1;}
    }
}
