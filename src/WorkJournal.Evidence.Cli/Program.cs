using System.Text.Json;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Services.Evidence;

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
