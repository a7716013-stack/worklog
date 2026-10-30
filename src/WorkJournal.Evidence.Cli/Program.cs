using System.Text.Json;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Research;

if(args.Length>0&&args[0].StartsWith("--pit-",StringComparison.Ordinal))return await PitCommands.Run(args);

if (args.Length>0 && args[0].StartsWith("--research",StringComparison.Ordinal))
{
    try
    {
        if(args.Length==3 && args[0]=="--research")
        {
            var request=JsonSerializer.Deserialize<ResearchRequest>(File.ReadAllText(args[1])) ?? throw new ArgumentException("Research request required.");
            Console.WriteLine(new ResearchStore(args[2]).Execute(request));return 0;
        }
        if(args.Length==3 && args[0]=="--research-replay" && Guid.TryParse(args[2],out var runId))
        {
            var report=new ResearchStore(args[1]).Replay(runId);
            Console.WriteLine($"REPLAY_MATCH {report.ResearchOutputDigest}; {report.PromotionStatus}");return 0;
        }
        if(args.Length==5 && args[0]=="--research-scorecard" && Guid.TryParse(args[2],out var scorecardRunId))
        {
            new ResearchStore(args[1]).Replay(scorecardRunId);
            var cards=JsonSerializer.Deserialize<DevelopmentScorecard[]>(File.ReadAllText(Path.Combine(args[1],scorecardRunId.ToString("N"),"development-scorecards.json")))??[];
            var card=cards.Single(c=>c.Fold==args[3]);
            using var stream=new FileStream(args[4],FileMode.CreateNew,FileAccess.Write);JsonSerializer.Serialize(stream,card);return 0;
        }
        if(args.Length==6 && args[0]=="--research-select" && DateTimeOffset.TryParse(args[2],System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var at))
        {
            var card=JsonSerializer.Deserialize<DevelopmentScorecard>(File.ReadAllText(args[1])) ?? throw new ArgumentException("Development scorecard required.");
            if(args[4]!="--output")throw new ArgumentException("Expected --output.");
            var selection=ResearchProtocolRules.Select(card,at,args[3]);
            using var stream=new FileStream(args[5],FileMode.CreateNew,FileAccess.Write);
            JsonSerializer.Serialize(stream,selection);Console.WriteLine("Selection recorded; no promotion.");return 0;
        }
        Console.Error.WriteLine("Usage: --research <request.json> <store-root> | --research-replay <store-root> <run-guid> | --research-scorecard <store-root> <run-guid> <fold> <new-file.json> | --research-select <scorecard.json> <ISO-time> <rationale> --output <new-file.json>");return 2;
    }
    catch(Exception e) when(e is ArgumentException or IOException or InvalidDataException or InvalidOperationException or JsonException or OverflowException)
    {Console.Error.WriteLine(e.Message);return 1;}
}

if (args.Length != 4 || !Guid.TryParse(args[2],out var instrument))
{
    Console.Error.WriteLine("Usage: WorkJournal.Evidence.Cli <dataset.json> <parameters.json> <instrument-guid> <new-output.json>");
    Console.Error.WriteLine("Offline research candidate only. No providers, database access, or production promotion.");
    return 2;
}
try
{
    var data = JsonSerializer.Deserialize<CandidateDataset>(File.ReadAllText(args[0])) ?? throw new ArgumentException("Dataset required.");
    var parameters = JsonSerializer.Deserialize<V3EvidenceCandidateV1ParameterSet>(File.ReadAllText(args[1])) ?? throw new ArgumentException("Parameters required.");
    var trace = new V3CandidateCalculator().Calculate(data,instrument,parameters);
    // CreateNew prevents silent replacement of previous research evidence.
    using var output = new FileStream(args[3],FileMode.CreateNew,FileAccess.Write);
    JsonSerializer.Serialize(output,trace,new JsonSerializerOptions {WriteIndented=true});
    Console.WriteLine($"{trace.ValidationStatus}; {trace.Score.Status}; AvailableWeight={trace.Score.AvailableWeight}; CalculationId={trace.CalculationId}");
    return 0; // Incomplete evidence is a valid recorded research result, not a transport failure.
}
catch (Exception e) when (e is ArgumentException or JsonException or IOException or InvalidOperationException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
