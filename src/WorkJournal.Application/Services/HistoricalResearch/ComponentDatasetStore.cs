using System.Reflection;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.HistoricalResearch.Components;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Research;
using Receipt=WorkJournal.Web.Models.HistoricalResearch.Components.ComponentDatasetApprovalReceipt;
namespace WorkJournal.Web.Services.HistoricalResearch;

public sealed class ComponentDatasetStore(string root,string evidenceRoot,string authorityRoot,string closureRoot)
{
    private string Folder(Guid id)=>Path.Combine(root,"approvals",id.ToString("N"));
    public ComponentDatasetEvaluation Evaluate(ComponentDatasetInput input)=>new ComponentDatasetGate(evidenceRoot,authorityRoot,closureRoot).Evaluate(input);
    public void Failure(string stage,string reason,string inputDigest)
    {
        var dir=Path.Combine(root,"failures");Directory.CreateDirectory(dir);
        HistoricalDatasetStore.WriteNew(Path.Combine(dir,Guid.NewGuid().ToString("N")+".json"),new ComponentFailure(stage,reason,inputDigest,"NOT_APPROVED","SEE_EVALUATION_OR_UNKNOWN",DateTimeOffset.UtcNow));
    }
    public Receipt Approve(ComponentDatasetInput input)
    {
        var e=Evaluate(input);
        if(e.Status!="READY_FOR_EXPLICIT_APPROVAL"){Failure("APPROVAL",string.Join(';',e.KnownGaps),e.InputDigest);throw new InvalidOperationException("BLOCKED_BY_AUTHORITY_OR_DATA");}
        var d=input.Closure.Dataset.Source.Dataset;var cases=new AuthorityClosureStore(closureRoot,evidenceRoot).Evaluate(input.Closure);
        var evidence=cases.Cases.SelectMany(c=>c.Case.EvidenceIds).Distinct().Select(id=>new EvidenceAcquisitionStore(evidenceRoot).ReadArtifact(id).SHA256).Order(StringComparer.Ordinal).ToArray();
        var receipt=new Receipt(new Guid(Convert.FromHexString(e.InputDigest)[..16]),input.Request.Component,input.Request.CandidateVersion,input.Request.ParameterSetId,
            e.DatasetSnapshotId,input.Universes.Select(u=>u.UniverseSnapshotId).Order().ToArray(),input.Request.PriceBasisVersion,
            cases.Cases.Select(c=>c.Digest).Order(StringComparer.Ordinal).ToArray(),d.Policies.Select(p=>p.Version).Distinct().Order(StringComparer.Ordinal).ToArray(),evidence,
            input.Request.CoverageStart,input.Request.CoverageEnd,input.Request.RequestedAt,input.Request.RequestedBy,e.InputDigest,e.DatasetDigest,e.Coverage.Digest,"");
        receipt=receipt with{ApprovalDigest=ResearchMath.Digest(receipt)};
        var dir=Folder(receipt.ApprovalId);Directory.CreateDirectory(dir);
        HistoricalDatasetStore.WriteNew(Path.Combine(dir,"archive.json"),new ComponentApprovalArchive(input,e,receipt));
        // Freeze original bytes and review histories, not just parsed values or mutable paths.
        Copy(evidenceRoot,Path.Combine(dir,"evidence"));Copy(authorityRoot,Path.Combine(dir,"authority"));Copy(closureRoot,Path.Combine(dir,"closure"));
        var replay=new ComponentDatasetGate(Path.Combine(dir,"evidence"),Path.Combine(dir,"authority"),Path.Combine(dir,"closure")).Evaluate(input);
        if(replay.Digest!=e.Digest)throw new InvalidDataException("REPLAY_MISMATCH: authority changed during freeze.");
        Seal(dir);return receipt;
    }
    private static void Copy(string from,string to)
    {
        if(Path.GetFullPath(to).StartsWith(Path.GetFullPath(from).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Approval store must not be inside an evidence source store.");
        Directory.CreateDirectory(to);
        foreach(var f in Directory.GetFiles(from,"*",SearchOption.AllDirectories))
        {
            if((File.GetAttributes(f)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked archive entries forbidden.");
            var relative=Path.GetRelativePath(from,f);var target=Path.Combine(to,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(f,target,false);
        }
    }
    private static Dictionary<string,string> Digests(string dir)=>Directory.GetFiles(dir,"*",SearchOption.AllDirectories)
        .Where(f=>f!=Path.Combine(dir,"manifest.json")).Order(StringComparer.Ordinal).ToDictionary(f=>Path.GetRelativePath(dir,f).Replace('\\','/'),f=>EvidenceAcquisitionStore.BytesDigest(File.ReadAllBytes(f)));
    private static void Seal(string dir)=>HistoricalDatasetStore.WriteNew(Path.Combine(dir,"manifest.json"),Digests(dir));
    private static void Verify(string dir)
    {
        var saved=HistoricalDatasetStore.Read<Dictionary<string,string>>(Path.Combine(dir,"manifest.json"));
        if(ResearchMath.Digest(saved)!=ResearchMath.Digest(Digests(dir)))throw new InvalidDataException("REPLAY_MISMATCH: archive bytes changed.");
    }
    public ComponentApprovalArchive Replay(Guid id)
    {
        var dir=Folder(id);Verify(dir);var a=HistoricalDatasetStore.Read<ComponentApprovalArchive>(Path.Combine(dir,"archive.json"));
        var e=new ComponentDatasetGate(Path.Combine(dir,"evidence"),Path.Combine(dir,"authority"),Path.Combine(dir,"closure")).Evaluate(a.Input);
        if(a.Receipt.ApprovalId!=id||e.Status!="READY_FOR_EXPLICIT_APPROVAL"||e.Digest!=a.Evaluation.Digest||a.Receipt.InputDigest!=e.InputDigest||
            a.Receipt.DatasetDigest!=e.DatasetDigest||a.Receipt.CoverageReportDigest!=e.Coverage.Digest||a.Receipt.ApprovalDigest!=ResearchMath.Digest(a.Receipt with{ApprovalDigest=""}))
            throw new InvalidDataException("REPLAY_MISMATCH: approval receipt.");
        return a;
    }
    public ApprovalUseRevision Revoke(Guid id,string status,string reason,DateTimeOffset at)
    {
        var a=Replay(id);if(status is not "SUPERSEDED" and not "REVOKED_FOR_FUTURE_USE"||string.IsNullOrWhiteSpace(reason)||at<a.Receipt.ApprovedAt)throw new ArgumentException("Explicit revocation required.");
        var dir=Path.Combine(root,"use-status",id.ToString("N"));Directory.CreateDirectory(dir);
        if(Directory.GetFiles(dir).Length!=0)throw new InvalidOperationException("Already revoked; historical receipt remains frozen.");
        var revision=new ApprovalUseRevision(0,a.Receipt.ApprovalDigest,id,status,at,reason,"");revision=revision with{Digest=ResearchMath.Digest(revision)};
        HistoricalDatasetStore.WriteNew(Path.Combine(dir,"00000000.json"),revision);return revision;
    }
    public ComponentApprovalArchive Current(Guid id)
    {
        var a=Replay(id);if(Directory.Exists(Path.Combine(root,"use-status",id.ToString("N"))))throw new InvalidOperationException("STALE_APPROVAL_RECEIPT: revoked for future use.");
        var current=Evaluate(a.Input);if(current.Status!="READY_FOR_EXPLICIT_APPROVAL"||current.Digest!=a.Evaluation.Digest)throw new InvalidOperationException("STALE_APPROVAL_RECEIPT");return a;
    }
    private static void Code(string sha)
    {
        var version=typeof(ComponentDatasetStore).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??"";
        if(sha.Length!=40||!sha.All(Uri.IsHexDigit)||!version.EndsWith("+"+sha,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Code SHA must match the built assembly SourceRevisionId.");
    }
    public string Baseline(Guid approvalId,ResearchRequest template,string codeSha)
    {
        var a=Current(approvalId);Code(codeSha);
        if(template.CandidateVersion!=a.Receipt.CandidateVersion||template.EvaluateOos||template.CreatedAt<a.Receipt.ApprovedAt)throw new InvalidOperationException("Candidate-v1 development-only baseline required.");
        var closure=new AuthorityClosureStore(closureRoot,evidenceRoot).Evaluate(a.Input.Closure);
        var runs=Path.Combine(root,"baselines");var dir=new RealBaselineStore(runs).Execute(a.Input.Closure.Dataset,template,evidenceRoot,authorityRoot,TimeProvider.System,closure,closureRoot);
        var d=a.Input.Closure.Dataset.Source.Dataset;
        var manifest=new ComponentBaselineManifest(template.ResearchRunId,approvalId,a.Receipt.ApprovalDigest,a.Receipt.Component,a.Receipt.CandidateVersion,a.Receipt.ParameterSetId,
            a.Receipt.DatasetSnapshotId,a.Receipt.UniverseSnapshotIds,d.Benchmarks.Select(b=>b.BenchmarkVersionId).Distinct().Order().ToArray(),a.Receipt.AuthorityClosureRevisions,
            a.Receipt.AvailabilityPolicyVersions,a.Receipt.PriceBasisVersion,a.Receipt.EvidenceDigests,codeSha,template.CreatedAt,"");
        manifest=manifest with{ManifestDigest=ResearchMath.Digest(manifest)};
        // Separate wrapper directory: the existing baseline manifest is never rewritten.
        var wrapper=Path.Combine(root,"component-runs",template.ResearchRunId.ToString("N"));Directory.CreateDirectory(wrapper);
        HistoricalDatasetStore.WriteNew(Path.Combine(wrapper,"identity.json"),manifest);
        HistoricalDatasetStore.WriteNew(Path.Combine(wrapper,"report.json"),BaselineReport(manifest));Seal(wrapper);return wrapper;
    }
    private object BaselineReport(ComponentBaselineManifest m)
    {
        var baseline=new RealBaselineStore(Path.Combine(root,"baselines")).Replay(m.ResearchRunId);
        var archive=HistoricalDatasetStore.Read<BaselineArchive>(Path.Combine(root,"baselines",m.ResearchRunId.ToString("N"),"archive.json"));
        var research=new ResearchEngine().Run(archive.Request);
        bool Included(string variable)=>variable==m.Component.ToString()||(m.Component==WorkJournal.Web.Models.Evidence.EvidenceCode.Trend&&variable.StartsWith("Ablation.TrendWithout",StringComparison.Ordinal));
        var metrics=research.Metrics.Where(x=>Included(x.Variable)).ToArray();
        return new{DataMode="RealApproved",Status=baseline.Run.Status=="BASELINE_COMPLETED"?"REAL_BASELINE_COMPLETED":"BLOCKED_BY_DATA",PredictiveAbility="OBSERVED",
            Manifest=m,Baseline=baseline,Metrics=metrics,Groups=research.GroupMetrics.Where(g=>Included(g.Metric.Variable)).ToArray(),
            Ablation=metrics.Where(x=>x.Variable.StartsWith("Ablation.")).Select(x=>new{x.Variable,x.Fold,x.Partition,x.Horizon,
                DeltaIC=x.MeanIc-metrics.Single(y=>y.Variable==m.Component.ToString()&&y.Fold==x.Fold&&y.Partition==x.Partition&&y.Horizon==x.Horizon).MeanIc,
                DeltaRankIC=x.MeanRankIc-metrics.Single(y=>y.Variable==m.Component.ToString()&&y.Fold==x.Fold&&y.Partition==x.Partition&&y.Horizon==x.Horizon).MeanRankIc,x.Distribution}).ToArray(),
            FullEvidence100=(decimal?)null,V3Promotion="NOT_PROMOTED",V3Threshold="UNRESOLVED",Phase5="LOCKED"};
    }
    public object BaselineReplay(Guid runId)
    {
        var dir=Path.Combine(root,"component-runs",runId.ToString("N"));Verify(dir);
        var m=HistoricalDatasetStore.Read<ComponentBaselineManifest>(Path.Combine(dir,"identity.json"));Code(m.CodeCommitSha);var approval=Replay(m.ApprovalId);
        if(m.ResearchRunId!=runId||m.ManifestDigest!=ResearchMath.Digest(m with{ManifestDigest=""})||m.ApprovalDigest!=approval.Receipt.ApprovalDigest)throw new InvalidDataException("REPLAY_MISMATCH");
        var result=BaselineReport(m);var saved=HistoricalDatasetStore.Read<System.Text.Json.JsonElement>(Path.Combine(dir,"report.json"));
        if(ResearchMath.Digest(result)!=ResearchMath.Digest(saved))throw new InvalidDataException("REPLAY_MISMATCH");return result;
    }
}
