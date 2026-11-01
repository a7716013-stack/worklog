using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.HistoricalResearch.Components;
using WorkJournal.Web.Models.HistoricalResearch.Resolution;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;

internal static class ResolutionCommands
{
    public static async Task<int> Run(string[] args)
    {
        T Read<T>(string path) => HistoricalDatasetStore.Read<T>(path);
        void Write(string path, object value) => HistoricalDatasetStore.WriteNew(path, value);
        try
        {
            if (args.Length == 3 && args[0] == "--resolution-request") { Write(args[2], AuthorityResolution.Create(Read<ComponentDatasetInput>(args[1]))); return 0; }
            if (args.Length == 8 && args[0] is "--resolution-evaluate" or "--resolution-approve")
            {
                var store = new AuthorityResolutionStore(args[2], args[3], args[4], args[5]);
                var request = Read<TrendResolutionRequest>(args[1]);
                if (args[0] == "--resolution-approve") { Write(args[7], store.Approve(request, args[6])); return 0; }
                if (args[6] != "--output") throw new ArgumentException("Expected --output.");
                var report = store.Execute(request); Write(args[7], report); return report.Status == "READY_FOR_EXPLICIT_APPROVAL" ? 0 : 1;
            }
            if (args.Length == 7 && args[0] is "--resolution-replay" or "--resolution-baseline-replay")
            {
                var store = new AuthorityResolutionStore(args[2], args[3], args[4], args[5]);
                Write(args[6], args[0] == "--resolution-replay" ? store.Replay(Guid.Parse(args[1])) : store.BaselineReplay(Guid.Parse(args[1]))); return 0;
            }
            if (args.Length == 9 && args[0] == "--resolution-baseline")
            { Console.WriteLine(new AuthorityResolutionStore(args[4], args[5], args[6], args[7]).Baseline(Guid.Parse(args[1]), Read<ResearchRequest>(args[2]), args[3])); return 0; }
            if (args.Length == 9 && args[0] == "--resolution-revoke")
            { new AuthorityResolutionStore(args[2], args[3], args[4], args[5]).Revoke(Guid.Parse(args[1]), args[6], args[7], DateTimeOffset.Parse(args[8])); return 0; }
            if (args.Length == 9 && args[0] == "--resolution-scope")
            {
                var result = new AuthorityResolutionStore(args[3], "", "", "").Reduce(Read<TrendResolutionRequest>(args[1]), Read<TrendResolutionRequest>(args[2]),
                    args[4], Read<string[]>(args[5]), args[6], DateTimeOffset.Parse(args[7])); Write(args[8], result); return 0;
            }
            if (args.Length == 4 && args[0] == "--resolution-scope-replay")
            { Write(args[3], new AuthorityResolutionStore(args[2], "", "", "").ReplayScope(Guid.Parse(args[1]))); return 0; }
            if (args.Length == 7 && args[0] == "--resolution-acquire")
            {
                using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
                var result = await new AuthorityResolutionLedger(args[3], args[4]).Acquire(Read<AuthorityResolutionTask>(args[1]), args[2], args[5], client, TimeProvider.System);
                Write(args[6], result); return result.ResultStatus == ResolutionStatus.CAPTURED ? 0 : 1;
            }
            if (args.Length == 4 && args[0] == "--resolution-not-found")
            { new AuthorityResolutionLedger(args[2], args[3]).NotFound(Read<EvidenceSearchResult>(args[1])); return 0; }
            throw new ArgumentException("Usage: --resolution-request <component-input> <new-file> | --resolution-evaluate <request> <store> <evidence> <authority> <closure> --output <new-file> | --resolution-approve <request> <store> <evidence> <authority> <closure> <code-sha> <new-file> | --resolution-replay/--resolution-baseline-replay <id> <store> <evidence> <authority> <closure> <new-file> | --resolution-baseline <approval-id> <template> <sha> <store> <evidence> <authority> <closure> --run | --resolution-scope <old> <new> <store> <reason> <gap-array-file> <reviewer> <at> <new-file> | --resolution-scope-replay <request-id> <store> <new-file> | --resolution-acquire <task> <url> <store> <evidence> <operator> <new-file> | --resolution-not-found <search-result> <store> <evidence>");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or FormatException)
        { Console.Error.WriteLine("RESOLUTION_BLOCKED: " + e.Message); return 1; }
    }
}
