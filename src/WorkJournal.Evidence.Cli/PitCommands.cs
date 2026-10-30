using System.Text.Json;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;

internal static class PitCommands
{
    public static async Task<int> Run(string[] args)
    {
        try
        {
            if(args.Length==5&&args[0]=="--pit-capture")
            {
                using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(60)};
                var kind=Enum.Parse<HistoricalDatasetType>(args[1],false);
                var date=DateOnly.ParseExact(args[2],"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
                var capture=await new OfficialHistoricalSource(client,TimeProvider.System).CaptureAsync(kind,date,args[3]);
                HistoricalDatasetStore.WriteNew(args[4],capture);
                Console.WriteLine($"CAPTURED {capture.Facts.Length}; AvailableAt UNKNOWN; {capture.Archive.SourceChecksum}");return 0;
            }
            if(args.Length==3&&args[0]=="--pit-build")
            {
                var result=new HistoricalDatasetStore(args[2]).Execute(HistoricalDatasetStore.Read<HistoricalBuildRequest>(args[1]));
                Console.WriteLine($"{result.Status} {result.DatasetSnapshotId} {result.DatasetChecksum}");return 0;
            }
            if(args.Length==3&&args[0]=="--pit-replay")
            {var result=new HistoricalDatasetStore(args[1]).Replay(args[2]);Console.WriteLine($"REPLAY_MATCH {result.DatasetChecksum}; {result.Status}");return 0;}
            if(args.Length==5&&args[0]=="--pit-research")
            {Console.WriteLine(new HistoricalDatasetStore(args[1]).Research(args[2],HistoricalDatasetStore.Read<ResearchRequest>(args[3]),args[4]));return 0;}
            if(args.Length==3&&args[0]=="--pit-register")
            {new ResearchCandidateRegistry(args[2]).Register(HistoricalDatasetStore.Read<ResearchCandidateRegistration>(args[1]));Console.WriteLine("REGISTERED_IMMUTABLY");return 0;}
            if(args.Length==5&&args[0]=="--pit-report"&&Guid.TryParse(args[2],out var reportId))
            {
                var store=new ResearchStore(args[1]);var report=store.Replay(reportId);
                var request=HistoricalDatasetStore.Read<ResearchRequest>(Path.Combine(args[1],reportId.ToString("N"),"request.json"));
                var history=Directory.EnumerateDirectories(args[1]).Select(Path.GetFileName).Where(n=>Guid.TryParseExact(n,"N",out _)).Select(n=>store.Replay(Guid.Parse(n!))).ToArray();
                HistoricalDatasetStore.WriteNew(args[4],EvidenceValidationReport.Create(request,report,new ResearchCandidateRegistry(args[3]).ReadAll(),history));return 0;
            }
            Console.Error.WriteLine("Usage: --pit-capture <Price|Institutional> <yyyy-MM-dd> <symbol> <new-capture.json> | --pit-build <request.json> <store> | --pit-replay <store> <snapshot-N-guid> | --pit-research <store> <snapshot-N-guid> <template.json> <research-store> | --pit-register <candidate.json> <registry> | --pit-report <research-store> <run-guid> <registry> <new-report.json>");return 2;
        }
        catch(Exception e) when(e is ArgumentException or IOException or InvalidOperationException or InvalidDataException or JsonException or HttpRequestException or TaskCanceledException or FormatException)
        {Console.Error.WriteLine(e.Message);return 1;}
    }
}
