using System.Text.Json;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

public sealed class HistoricalDatasetStore(string root)
{
    public HistoricalBuildResult Execute(HistoricalBuildRequest request)
    {
        var canonical=HistoricalDatasetBuilder.Canonicalize(request);
        var result=new HistoricalDatasetBuilder().Build(canonical);
        Directory.CreateDirectory(root);
        var target=Path.Combine(root,result.DatasetSnapshotId);
        if(Directory.Exists(target))throw new IOException("Dataset already exists; replay instead of overwrite.");
        var pending=Path.Combine(root,".pending-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(pending);
        // Preserve decimal serialization for downstream parameter identities.
        File.WriteAllText(Path.Combine(pending,"request.json"),JsonSerializer.Serialize(canonical));
        File.WriteAllText(Path.Combine(pending,"result.json"),ResearchMath.Canonical(result));
        File.WriteAllText(Path.Combine(pending,"manifest.json"),ResearchMath.Canonical(Manifest(canonical,result)));
        Directory.Move(pending,target);return result;
    }
    public HistoricalBuildResult Replay(string snapshotId)
    {
        if(!Guid.TryParseExact(snapshotId,"N",out _))throw new ArgumentException("Dataset snapshot GUID in N format required.");
        var directory=Path.Combine(root,snapshotId);
        var request=Read<HistoricalBuildRequest>(Path.Combine(directory,"request.json"));
        var saved=Read<HistoricalBuildResult>(Path.Combine(directory,"result.json"));
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"manifest.json")));
        var replay=new HistoricalDatasetBuilder().Build(request);
        if(replay.DatasetSnapshotId!=snapshotId||ResearchMath.Digest(saved)!=ResearchMath.Digest(replay)||
            manifest.RootElement.GetProperty("ResultDigest").GetString()!=ResearchMath.Digest(saved)||
            manifest.RootElement.GetProperty("RequestDigest").GetString()!=ResearchMath.Digest(request)||
            manifest.RootElement.GetProperty("DatasetChecksum").GetString()!=replay.DatasetChecksum||
            ResearchMath.Canonical(manifest.RootElement)!=ResearchMath.Canonical(Manifest(request,replay)))
            throw new InvalidDataException("Dataset archive/replay mismatch.");
        return replay;
    }
    private static object Manifest(HistoricalBuildRequest request,HistoricalBuildResult result)=>new
    {
        result.DatasetSnapshotId,result.DatasetChecksum,result.InputDigest,ResultDigest=ResearchMath.Digest(result),RequestDigest=ResearchMath.Digest(request),
        StartDate=request.Dates.Min(x=>x.TradeDate),EndDate=request.Dates.Max(x=>x.TradeDate),request.PricePolicy,
        AvailabilityPolicyVersions=request.Policies.Select(x=>new{x.PolicyId,x.Version}),SourceChecksums=request.Archives.Select(x=>x.SourceChecksum)
    };
    public static T Read<T>(string file)=>JsonSerializer.Deserialize<T>(File.ReadAllText(file))??throw new InvalidDataException("Missing JSON object.");
    public static void WriteNew<T>(string file,T value)
    {using var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write);JsonSerializer.Serialize(stream,value,new JsonSerializerOptions{WriteIndented=true});}
    public string Research(string snapshotId,ResearchRequest template,string researchRoot)
    {
        var dataset=Replay(snapshotId);
        if(dataset.Snapshots.Length==0)throw new InvalidOperationException("BLOCKED_BY_DATA: no eligible research snapshots.");
        if(template.Snapshots.Length>0)throw new ArgumentException("Template snapshots must be empty; use archived PIT dataset only.");
        // Existing engine validates parameter checksum, label basis, split/purge and frozen OOS receipts.
        return new ResearchStore(researchRoot).Execute(template with{Snapshots=dataset.Snapshots});
    }
}

// Append-only immutable registrations; rejected/failed entries remain visible.
public sealed class ResearchCandidateRegistry(string root)
{
    public void Register(ResearchCandidateRegistration candidate)
    {
        if(candidate.ParameterSetId==Guid.Empty||candidate.FeatureVersions.IsDefaultOrEmpty||candidate.NormalizationVersions.IsDefaultOrEmpty||
           new[]{candidate.CandidateVersion,candidate.EvidenceCode,candidate.ParameterChecksum,candidate.Reason}.Any(string.IsNullOrWhiteSpace)||
           !new[]{"CANDIDATE","RESEARCHING","REJECTED","BLOCKED_BY_DATA","NOT_ENOUGH_EVIDENCE"}.Contains(candidate.Status))
            throw new ArgumentException("Explicit candidate identity, versions, reason and non-promoted research status required.");
        Directory.CreateDirectory(root);
        // One immutable identity, including rejected registrations; no hidden replacement of v1.
        var identity=ResearchMath.Digest(new{candidate.CandidateVersion,candidate.EvidenceCode,candidate.ParameterSetId});
        HistoricalDatasetStore.WriteNew(Path.Combine(root,identity+".json"),candidate);
    }
    public ResearchCandidateRegistration[] ReadAll()=>!Directory.Exists(root)?[]:Directory.EnumerateFiles(root,"*.json")
        .Select(HistoricalDatasetStore.Read<ResearchCandidateRegistration>).OrderBy(x=>x.CandidateVersion,StringComparer.Ordinal)
        .ThenBy(x=>x.EvidenceCode,StringComparer.Ordinal).ThenBy(x=>x.ParameterSetId).ToArray();
}
