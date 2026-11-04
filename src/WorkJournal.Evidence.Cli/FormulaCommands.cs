using System.Collections.Immutable;
using System.Text.Json;
using WorkJournal.Domain.Research;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Research;

public static class FormulaCommands
{
    public static int Run(string[] args)
    {
        try
        {
            if(args.Length==2&&args[0]=="--formula-identity") {Write(args[1],FormulaCatalog.Identity);return 0;}
            if(args.Length==8&&args[0]=="--formula-report")
            {
                var run=Guid.Parse(args[2]);var foldName=args[3];
                var partition=Enum.Parse<ResearchPartition>(args[4],true);var horizon=int.Parse(args[5]);
                if(args[6]!="--output")throw new ArgumentException("--output required");
                // Existing archive replay verifies approved real-data authorization and OOS receipts.
                var report=new ResearchStore(args[1]).Replay(run);
                var request=Read<ResearchRequest>(Path.Combine(args[1],run.ToString("N"),"request.json"));
                if(!request.Protocol.OutcomeHorizons.Contains(horizon)||!Enum.IsDefined(partition)||partition==ResearchPartition.Oos&&!request.EvaluateOos)throw new ArgumentException("Undeclared horizon/partition");
                var fold=request.Protocol.Folds.Single(x=>x.Code==foldName);var period=ResearchProtocolRules.Period(fold,partition);
                var observations=report.Observations.Where(x=>x.Trace.Identity.TradeDate>=period.Start&&x.Trace.Identity.TradeDate<=period.End)
                    .Select(x=>x with {Outcomes=x.Outcomes.Select(o=>o.LabelEnd>period.End?o with {FutureReturn=null,FutureMaxCloseReturn=null,FutureMinCloseReturn=null,Status="PURGED_PARTITION_BOUNDARY"}:o).ToImmutableArray()});
                var package=FormulaDiagnostics.Build(observations,horizon,request.Protocol.Quantiles,foldName+"/"+partition);
                Write(args[7],new {run,SourceDigest=report.ResearchOutputDigest,Synthetic=request.Snapshots.All(x=>x.Evidence.Synthetic),Package=package});return 0;
            }
            if(args.Length==4&&args[0]=="--formula-review")
            {
                var hypothesis=Read<CandidateHypothesis>(args[1]);var comparison=Read<CandidateComparison>(args[2]);
                CandidateGovernance.Validate(hypothesis,comparison);Write(args[3],new {hypothesis,comparison,Digest=ResearchMath.Digest(new{hypothesis,comparison})});return 0;
            }
            if(args.Length==6&&args[0]=="--formula-freeze")
            {
                Write(args[5],CandidateGovernance.Freeze(Read<CandidateHypothesis>(args[1]),Read<CandidateComparison>(args[2]),DateTimeOffset.Parse(args[3]),DateOnly.Parse(args[4])));return 0;
            }
            Console.Error.WriteLine("--formula-identity <new.json> | --formula-report <store> <run-id> <fold> <Train|Validation|Oos> <horizon> --output <new.json> | --formula-review <hypothesis.json> <comparison.json> <new.json> | --formula-freeze <hypothesis.json> <comparison.json> <timestamp> <oos-start> <new.json>");return 2;
        }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or IOException or JsonException or FormatException)
        {Console.Error.WriteLine(e.Message);return 1;}
    }
    private static T Read<T>(string path)=>JsonSerializer.Deserialize<T>(File.ReadAllText(path))??throw new InvalidDataException("Missing document");
    private static void Write<T>(string path,T value) {using var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write);JsonSerializer.Serialize(f,value,new JsonSerializerOptions{WriteIndented=true});}
}
