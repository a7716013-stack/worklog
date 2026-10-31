using System.Text.Json;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;

internal static class AcquisitionCommands
{
    public static async Task<int> Run(string[] args)
    {
        try
        {
            if(args.Length==4&&args[0]=="--evidence-task-state")
            {var state=new EvidenceAcquisitionStore(args[1]).TaskState(Guid.Parse(args[2]),Guid.Parse(args[3]));Console.WriteLine($"{state.Task.Status} {state.Digest}");return 0;}
            if(args.Length==10&&args[0]=="--evidence-task-review")
            {
                var state=new EvidenceAcquisitionStore(args[1]).Transition(Guid.Parse(args[2]),Guid.Parse(args[3]),args[4],Enum.Parse<AcquisitionStatus>(args[5]),
                    DateTimeOffset.Parse(args[6],System.Globalization.CultureInfo.InvariantCulture),args[7],args[8],args[9]=="-"?null:args[9]);
                Console.WriteLine($"{state.Task.Status} {state.Digest}");return 0;
            }
            if(args.Length==3&&args[0]=="--evidence-acquire")
            {
                using var handler=new HttpClientHandler{AllowAutoRedirect=false};
                using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(30)};
                http.DefaultRequestHeaders.UserAgent.ParseAdd("WorkJournal-Evidence/3.0.8");
                var m=await new EvidenceAcquisitionStore(args[2]).Acquire(HistoricalDatasetStore.Read<AcquisitionPlan>(args[1]),http,TimeProvider.System);
                Console.WriteLine($"{m.Status} {m.AcquisitionRunId} {m.OutputDigest}");return 0;
            }
            if(args.Length==3&&args[0]=="--evidence-replay")
            {var m=new EvidenceAcquisitionStore(args[1]).Replay(Guid.Parse(args[2]));Console.WriteLine($"REPLAY_MATCH {m.OutputDigest}");return 0;}
            if(args.Length==3&&args[0]=="--evidence-review")
            {var r=new EvidenceAcquisitionStore(args[2]).Review(HistoricalDatasetStore.Read<EvidenceReviewReceipt>(args[1]));Console.WriteLine($"REVIEW_RECORDED {r.ReviewDigest}");return 0;}
            if(args.Length==5&&args[0]=="--evidence-export-authority")
            {
                var store=new EvidenceAcquisitionStore(args[1]);var review=store.ReadReview(args[2]);
                var e=HistoricalDatasetStore.Read<HistoricalAvailabilityEvidence>(args[3]);var raw=store.ReadArtifact(review.EvidenceId);
                if(e.EvidenceId!=raw.EvidenceId||e.EvidenceDigest!=raw.SHA256||e.SourceIdentity!=raw.SourceReference||e.Provider!=raw.Provider||
                    e.EffectiveFrom<review.EffectiveFrom||e.EffectiveTo is null||e.EffectiveTo>review.EffectiveTo||e.EvidenceCapturedAt!=raw.CapturedAt||
                    e.VerificationStatus!=EvidenceVerificationStatus.Verified||!review.Supported.Contains("AvailabilityPolicy:"+e.ClaimedAvailabilityRule)||
                    review.Ambiguous.Contains("AvailabilityPolicy:"+e.ClaimedAvailabilityRule)||review.Unsupported.Contains("AvailabilityPolicy:"+e.ClaimedAvailabilityRule))
                    throw new InvalidOperationException("Reviewed evidence does not support this authority export.");
                new AuthorityStore(args[4]).Evidence(e);Console.WriteLine("REVIEWED_EVIDENCE_EXPORTED_AUTHORITY_STILL_REQUIRES_APPROVAL");return 0;
            }
            if(args.Length==2&&args[0]=="--evidence-dataset-digest")
            {Console.WriteLine(RealDataApproval.InputDigest(HistoricalDatasetStore.Read<RealDatasetSubmission>(args[1])));return 0;}
            if(args.Length==5&&args[0]=="--evidence-dataset-evaluate")
            {
                var s=HistoricalDatasetStore.Read<RealDatasetSubmission>(args[1]);var r=new RealDataApproval(args[2],args[3]).Evaluate(s);
                HistoricalDatasetStore.WriteNew(args[4],r);Console.WriteLine($"{r.Status} {r.ApprovalDigest}");return 0;
            }
            if(args.Length==6&&args[0]=="--evidence-baseline")
            {
                Console.WriteLine(new RealBaselineStore(args[5]).Execute(HistoricalDatasetStore.Read<RealDatasetSubmission>(args[1]),
                    HistoricalDatasetStore.Read<ResearchRequest>(args[2]),args[3],args[4],TimeProvider.System));return 0;
            }
            if(args.Length==3&&args[0]=="--evidence-baseline-replay")
            {var r=new RealBaselineStore(args[1]).Replay(Guid.Parse(args[2]));Console.WriteLine($"REPLAY_MATCH {r.Run.OutputDigest} {r.Run.Status}");return 0;}
            Console.Error.WriteLine("Usage: --evidence-acquire <plan.json> <evidence-store> | --evidence-replay <store> <run-id> | --evidence-review <review.json> <store> | --evidence-export-authority <evidence-store> <review-digest> <evidence.json> <authority-store> | --evidence-dataset-digest <submission.json> | --evidence-dataset-evaluate <submission.json> <evidence-store> <authority-store> <new-report.json> | --evidence-baseline <submission.json> <template.json> <evidence-store> <authority-store> <baseline-store> | --evidence-baseline-replay <baseline-store> <run-id>");return 2;
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or IOException or InvalidDataException or JsonException or FormatException)
        {Console.Error.WriteLine(ex.Message);return 1;}
    }
}
