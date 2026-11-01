using System.Text.Json;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.HistoricalResearch.Components;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Evidence;

internal static class ComponentCommands
{
    public static int Run(string[] args)
    {
        string failureRoot=Path.Combine("artifacts","component-cli");
        try
        {
            if(args.Length==5&&args[0]=="--component-link-price")
            {HistoricalDatasetStore.WriteNew(args[4],ComponentEvidence.LinkPrice(HistoricalDatasetStore.Read<ComponentDatasetInput>(args[1]),args[2],Guid.Parse(args[3])));return 0;}
            if(args.Length==4&&args[0]=="--component-dataset-request")
            {
                var closure=HistoricalDatasetStore.Read<ComponentClosureRequest>(args[1]);var d=closure.Dataset.Source.Dataset;
                if(closure.Dataset.RequestedComponents.Length!=1)throw new ArgumentException("Explicit single component required.");
                var request=new ComponentDatasetApprovalRequest(closure.Dataset.RequestedComponents[0],V3CandidateCalculator.Version,d.ParameterSetId,
                    d.Dates.Min(x=>x.TradeDate),d.Dates.Max(x=>x.TradeDate),d.Universes.Select(x=>x.DefinitionVersion).Distinct().SingleOrDefault()??"UNKNOWN",
                    d.PricePolicy.PriceBasisVersion,closure.Closures.Select(c=>c.CaseId).ToArray(),d.Policies.Where(p=>p.DatasetType==HistoricalDatasetType.Price).Select(p=>p.PolicyId).ToArray(),args[2],DateTimeOffset.UtcNow,"");
                request=request with{RequestDigest=ResearchMath.Digest(request)};
                HistoricalDatasetStore.WriteNew(args[3],new ComponentDatasetInput(ComponentDataMode.RealApproved,request,closure,[],[],[],[],[],[],[],[]));
                Console.WriteLine("REQUEST_ONLY: evidence fields must be supplied and verified; no approval.");return 0;
            }
            if(args.Length==5&&args[0] is "--authority-claims" or "--authority-conflicts")
            {
                var claims=ComponentEvidence.Claims(new AuthorityClosureStore(args[2],args[3]),Guid.Parse(args[1]));
                HistoricalDatasetStore.WriteNew(args[4],args[0]=="--authority-conflicts"?claims.Where(c=>c.SupportStatus==ClaimSupport.CONFLICTING).ToArray():claims);return 0;
            }
            if(args.Length==4&&args[0]=="--component-lineage")
            {HistoricalDatasetStore.WriteNew(args[3],ComponentEvidence.ParsePrice(args[1],Guid.Parse(args[2])));return 0;}
            if(args.Length==8&&args[0] is "--component-dataset-evaluate" or "--component-dataset-approve" or "--authority-coverage")
            {
                failureRoot=args[2];var store=new ComponentDatasetStore(args[2],args[3],args[4],args[5]);
                if(args[6]!="--output")throw new ArgumentException("Expected --output.");
                var input=HistoricalDatasetStore.Read<ComponentDatasetInput>(args[1]);
                if(args[0]=="--component-dataset-approve"){HistoricalDatasetStore.WriteNew(args[7],store.Approve(input));Console.WriteLine("COMPONENT_DATASET_APPROVED; MODEL_NOT_VALIDATED");return 0;}
                var evaluation=store.Evaluate(input);HistoricalDatasetStore.WriteNew(args[7],evaluation);
                Console.WriteLine("STATUS="+evaluation.Status);
                if(evaluation.Status!="READY_FOR_EXPLICIT_APPROVAL"){store.Failure("EVALUATE",string.Join(';',evaluation.KnownGaps),evaluation.InputDigest);return 1;}return 0;
            }
            if(args.Length==7&&args[0]=="--component-dataset-replay")
            {
                failureRoot=args[2];var a=new ComponentDatasetStore(args[2],args[3],args[4],args[5]).Replay(Guid.Parse(args[1]));HistoricalDatasetStore.WriteNew(args[6],a);return 0;
            }
            if(args.Length==10&&args[0]=="--component-baseline")
            {
                failureRoot=args[4];var store=new ComponentDatasetStore(args[4],args[5],args[6],args[7]);
                if(args[8]!="--output")throw new ArgumentException("Expected --output.");
                var path=store.Baseline(Guid.Parse(args[1]),HistoricalDatasetStore.Read<ResearchRequest>(args[2]),args[3]);
                HistoricalDatasetStore.WriteNew(args[9],new{Path=path,DataMode="RealApproved"});return 0;
            }
            if(args.Length==7&&args[0] is "--component-baseline-replay" or "--component-report")
            {
                failureRoot=args[2];HistoricalDatasetStore.WriteNew(args[6],new ComponentDatasetStore(args[2],args[3],args[4],args[5]).BaselineReplay(Guid.Parse(args[1])));return 0;
            }
            if(args.Length==9&&args[0]=="--component-dataset-revoke")
            {
                failureRoot=args[2];new ComponentDatasetStore(args[2],args[3],args[4],args[5]).Revoke(Guid.Parse(args[1]),args[6],args[7],DateTimeOffset.Parse(args[8]));return 0;
            }
            throw new ArgumentException("Usage: --component-dataset-request <closure.json> <requester> <new-file> | --component-lineage <evidence-store> <evidence-id> <new-file> | --authority-claims/--authority-conflicts <case-id> <closure-store> <evidence-store> <new-file> | --component-dataset-evaluate/--component-dataset-approve/--authority-coverage <input> <store> <evidence> <authority> <closure> --output <new-file> | --component-dataset-replay/--component-baseline-replay/--component-report <id> <store> <evidence> <authority> <closure> <new-file> | --component-baseline <approval-id> <template> <code-sha> <store> <evidence> <authority> <closure> --output <new-file> | --component-dataset-revoke <id> <store> <evidence> <authority> <closure> <status> <reason> <time>");
        }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or InvalidDataException or IOException or JsonException or FormatException or OverflowException)
        {
            new ComponentDatasetStore(failureRoot,"","","").Failure(args.FirstOrDefault()??"USAGE",e.Message,ResearchMath.Digest(args));
            Console.Error.WriteLine("STATUS=BLOCKED_BY_AUTHORITY_OR_DATA: "+e.Message);return 1;
        }
    }
}
