using System.Text.Json;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;

internal static class AuthorityCommands
{
    public static int Run(string[] args)
    {
        try
        {
            if(args.Length==3&&args[0]=="--authority-evidence")
            {new AuthorityStore(args[2]).Evidence(HistoricalDatasetStore.Read<HistoricalAvailabilityEvidence>(args[1]));Console.WriteLine("EVIDENCE_ARCHIVED_NOT_AUTOMATICALLY_APPROVED");return 0;}
            if(args.Length==3&&args[0]=="--authority-register")
            {var r=new AuthorityStore(args[2]).Register(HistoricalDatasetStore.Read<HistoricalDataAuthority>(args[1]));Console.WriteLine($"Draft {r.Digest}");return 0;}
            if(args.Length==8&&args[0]=="--authority-review")
            {
                var r=new AuthorityStore(args[1]).Review(Guid.Parse(args[2]),args[3],Enum.Parse<AuthorityStatus>(args[4],false),
                    DateTimeOffset.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture),args[6],args[7]);
                Console.WriteLine($"{r.Authority.Status} {r.Digest}");return 0;
            }
            if(args.Length==4&&args[0]=="--authority-export")
            {HistoricalDatasetStore.WriteNew(args[3],new AuthorityStore(args[1]).Read(Guid.Parse(args[2])).Authority);return 0;}
            if(args.Length==2&&args[0]=="--authority-digest")
            {Console.WriteLine(HistoricalAuthority.SubmissionDigest(HistoricalDatasetStore.Read<AuthoritySubmission>(args[1])));return 0;}
            if(args.Length==3&&args[0]=="--authority-policy-digest")
            {
                var dataset=HistoricalDatasetStore.Read<HistoricalBuildRequest>(args[1]);var id=Guid.Parse(args[2]);
                Console.WriteLine(HistoricalAuthority.PolicyScopeDigest(dataset,dataset.Policies.Single(p=>p.PolicyId==id)));return 0;
            }
            if(args.Length==3&&args[0]=="--authority-evaluate")
            {var r=new AuthorityStore(args[2]).EvaluateAndSave(HistoricalDatasetStore.Read<AuthoritySubmission>(args[1]));Console.WriteLine($"{r.Status} {r.ReportDigest}");return 0;}
            if(args.Length==3&&args[0]=="--authority-replay")
            {var r=new AuthorityStore(args[1]).Replay(args[2]);Console.WriteLine($"REPLAY_MATCH {r.Report.ReportDigest} {r.Report.Status}");return 0;}
            if(args.Length==5&&args[0]=="--authority-research")
            {Console.WriteLine(new AuthorityStore(args[1]).Research(args[2],HistoricalDatasetStore.Read<ResearchRequest>(args[3]),args[4]));return 0;}
            Console.Error.WriteLine("Usage: --authority-evidence <evidence.json> <store> | --authority-register <draft.json> <store> | --authority-review <store> <authority-guid> <expected-digest> <status> <ISO-time> <reviewer> <reason> | --authority-export <store> <authority-guid> <new-file> | --authority-policy-digest <dataset-request.json> <policy-guid> | --authority-digest <submission.json> | --authority-evaluate <submission.json> <store> | --authority-replay <store> <report-digest> | --authority-research <store> <report-digest> <template.json> <research-store>");return 2;
        }
        catch(Exception e) when(e is ArgumentException or IOException or InvalidDataException or InvalidOperationException or JsonException or FormatException)
        {Console.Error.WriteLine(e.Message);return 1;}
    }
}
