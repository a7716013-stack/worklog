using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Domain.Research;
public sealed record FormulaIdentity(string ModelVersion,string FormulaDigest,string FeatureDefinitionVersion,string EvidenceDefinitionVersion,string OuterWeightStatus);
public sealed record CalculationIdentity(string CalculationId,string ModelVersion,string FormulaDigest,Guid ParameterSetId,string ParameterChecksum,
    Guid DatasetSnapshotId,Guid UniverseSnapshotId,Guid BenchmarkVersionId,string InputDigest,string CodeCommitSha);
public static class FormulaCatalog
{
    private static readonly Lazy<string> digest=new(()=>{
        var assembly=typeof(V3CandidateCalculator).Assembly;
        var source=assembly.GetManifestResourceNames().Where(n=>n.EndsWith(".cs",StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .Select(n=>{using var reader=new StreamReader(assembly.GetManifestResourceStream(n)!);return new {Name=n,Source=reader.ReadToEnd().Replace("\r\n","\n")};}).ToArray();
        if(source.Length<5)throw new InvalidOperationException("Formula source manifest missing");
        return ResearchMath.Digest(source);
    });
    public static FormulaIdentity Identity => new(V3CandidateCalculator.Version,digest.Value,V3CandidateCalculator.Version,V3CandidateCalculator.Version,"LOCKED_CANDIDATE_V1");
    public static CalculationIdentity Identify(CandidateTrace trace,string codeCommitSha)
    {
        var id=trace.Identity;
        return new(ResearchMath.Digest(new {trace.CalculationId,FormulaDigest=Identity.FormulaDigest,trace.InputDigest,codeCommitSha}),trace.CandidateVersion,
            Identity.FormulaDigest,id.ParameterSetId,id.ParameterChecksum,id.DatasetSnapshotId,id.UniverseSnapshotId,id.BenchmarkVersionId,trace.InputDigest,codeCommitSha);
    }
}
