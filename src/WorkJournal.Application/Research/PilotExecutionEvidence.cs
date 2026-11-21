using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record IsolatedControl(string Code,string Status,string EvidenceDigest);
public sealed record IsolatedEnvironmentReceipt(string Environment,string StoreIdentity,string SourceCodeSha,string Operator,string Reviewer,
    DateTimeOffset AcceptedAt,DateTimeOffset ExpiresAt,string EvidenceClass,IsolatedControl[] Controls,string Digest);
public sealed record AcquisitionRunConsent(string ScopeId,string RunId,string EnvironmentDigest,string Operator,string Mode,
    DateTimeOffset AuthorizedAt,DateTimeOffset ExpiresAt,string EvidenceDigest,string Digest);
public sealed record PilotExecutionEvidence(IsolatedEnvironmentReceipt Environment,AcquisitionRunConsent Consent);

// Additional execution preconditions, not a rights/scope approval authority. Only externally pinned receipts are accepted.
public static class PilotExecutionRules
{
    public static readonly string[] Controls=["ENDPOINT_ALLOWLIST","DNS_TLS","NO_REDIRECT_PROXY","REQUEST_QUOTA","CONCURRENCY",
        "DURABLE_RESERVATION","CANCELLATION","TIMEOUT","RETRY","CIRCUIT_BREAKER","EXCLUSIVE_LOCK","CRASH_RECOVERY",
        "IMMUTABLE_ARCHIVE","DIGEST_VERIFICATION","REPLAY","SENSITIVE_DATA_FILTER","STORAGE_BUDGET"];
    public static string StoreIdentity(string root)
    {var path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));return ResearchMath.Digest(OperatingSystem.IsWindows()?path.ToUpperInvariant():path);}
    public static string[] Blockers(PilotScope scope,string run,string root,PilotTrust trust,PilotExecutionEvidence? evidence,DateTimeOffset now)
    {
        if(evidence is null)return ["ISOLATED_ACCEPTANCE_REQUIRED","EXPLICIT_RUN_AUTHORIZATION_REQUIRED"];
        var e=evidence.Environment;var c=evidence.Consent;var reasons=new List<string>();
        bool Trusted(string digest,string[]? pins)=>PilotScopeRules.Digest(digest)&&(pins??[]).Contains(digest)&&!trust.RevokedDigests.Contains(digest);
        if(e is null||e.Digest!=ResearchMath.Digest(e with{Digest=""})||!Trusted(e.Digest,trust.EnvironmentDigests)||
            e.Environment!=trust.Environment||!trust.Isolated||e.StoreIdentity!=StoreIdentity(root)||e.SourceCodeSha!=scope.Requests[0].SourceCodeSha||
            e.Operator!=scope.RequestedBy||string.IsNullOrWhiteSpace(e.Reviewer)||string.Equals(e.Reviewer,e.Operator,StringComparison.OrdinalIgnoreCase)||
            e.EvidenceClass!="Verified Isolated Environment"||e.AcceptedAt.Offset!=TimeSpan.Zero||e.ExpiresAt.Offset!=TimeSpan.Zero||
            e.AcceptedAt>now||e.ExpiresAt<=now||e.ExpiresAt<=e.AcceptedAt||e.Controls is null||e.Controls.Length!=Controls.Length||
            e.Controls.Select(x=>x.Code).Distinct().Count()!=Controls.Length||e.Controls.Any(x=>!Controls.Contains(x.Code)||x.Status!="PASS"||!PilotScopeRules.Digest(x.EvidenceDigest)))
            reasons.Add("ISOLATED_ACCEPTANCE_REQUIRED");
        if(c is null||c.Digest!=ResearchMath.Digest(c with{Digest=""})||!Trusted(c.Digest,trust.ExecutionDigests)||
            c.ScopeId!=scope.Id||c.RunId!=run||!PilotScopeRules.Digest(run)||c.EnvironmentDigest!=e?.Digest||c.Operator!=scope.RequestedBy||
            c.Mode!="ACQUIRE_ONCE"||c.AuthorizedAt.Offset!=TimeSpan.Zero||c.ExpiresAt.Offset!=TimeSpan.Zero||c.AuthorizedAt>now||
            c.ExpiresAt<=now||c.ExpiresAt>scope.ExpiresAt||c.ExpiresAt>e?.ExpiresAt||c.ExpiresAt<=c.AuthorizedAt||!PilotScopeRules.Digest(c.EvidenceDigest))
            reasons.Add("EXPLICIT_RUN_AUTHORIZATION_REQUIRED");
        return reasons.ToArray();
    }
    public static void Require(PilotScope scope,string run,string root,PilotTrust trust,PilotExecutionEvidence? evidence,DateTimeOffset now)
    {if(Blockers(scope,run,root,trust,evidence,now).Length!=0)throw new InvalidOperationException("PILOT_EXECUTION_AUTHORIZATION_BLOCKED");}
}
