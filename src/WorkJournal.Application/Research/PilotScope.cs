using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record PilotScope(string Id,string Market,string[] Instruments,string[] Datasets,DateOnly HistoricalStart,DateOnly HistoricalEnd,
    DateTimeOffset ForwardStart,DateTimeOffset ForwardEnd,int FeatureLookback,int OutcomeHorizon,string Provider,int MaxRequests,
    long StorageLimitBytes,int MaximumResponseBytes,DateTimeOffset ExpiresAt,string RequestedBy,CapturePlan[] Requests,string[] RightsDigests);
public sealed record PilotApproval(string ScopeId,string Environment,string RequestedBy,string Reviewer,DateTimeOffset ValidFrom,
    DateTimeOffset ExpiresAt,string Status,string EvidenceDigest,string Digest);
// This is an operator-supplied trust boundary, stored outside the repository and independently provisioned.
// No command in this release creates or approves grants. Revocation always overrides a trusted digest.
public sealed record PilotTrust(string Environment,bool Isolated,string[] PermissionDigests,string[] ApprovalDigests,string[] RevokedDigests,
    string[]? EnvironmentDigests=null,string[]? ExecutionDigests=null);
public sealed record PilotAuthorization(CapturePermission[] Permissions,PilotApproval[] Approvals);
public static class PilotScopeRules
{
    public static string Id(PilotScope s)=>ResearchMath.Digest(s with{Id=""});
    public static bool Digest(string s)=>s is not null&&s.Length==64&&s.All(Uri.IsHexDigit);
    public static void Validate(PilotScope s)
    {
        if(s.Id!=Id(s)||s.Market is not ("TWSE" or "TPEx")||s.Provider!=s.Market||s.HistoricalStart>s.HistoricalEnd||
            s.ForwardStart.Offset!=TimeSpan.Zero||s.ForwardEnd.Offset!=TimeSpan.Zero||s.ExpiresAt.Offset!=TimeSpan.Zero||
            s.ForwardEnd<=s.ForwardStart||s.ExpiresAt>s.ForwardEnd||s.ExpiresAt<=s.ForwardStart||s.FeatureLookback<1||s.OutcomeHorizon<1||
            s.MaxRequests is <1 or >10000||s.MaximumResponseBytes is <1 or >AcquisitionSafety.MaximumBytes||
            s.StorageLimitBytes<s.MaximumResponseBytes||s.StorageLimitBytes>1024L*1024*1024||string.IsNullOrWhiteSpace(s.RequestedBy)||
            s.Instruments.Length==0||s.Instruments.Distinct().Count()!=s.Instruments.Length||s.Datasets.Length==0||s.Datasets.Distinct().Count()!=s.Datasets.Length||
            s.Requests.Length==0||s.Requests.Length>s.MaxRequests||s.Requests.Select(p=>p.Id).Distinct().Count()!=s.Requests.Length||
            s.RightsDigests.Any(x=>!Digest(x))||s.RightsDigests.Distinct().Count()!=s.RightsDigests.Length)
            throw new InvalidDataException("PILOT_SCOPE_INVALID");
        foreach(var p in s.Requests)
        {
            ForwardCaptureStore.ValidatePlan(p);
            if(p.Provider!=s.Provider||!s.Instruments.Contains(p.Instrument)||!s.Datasets.Contains(p.Dataset)||
                p.CoverageStart!=s.HistoricalStart||p.CoverageEnd!=s.HistoricalEnd||p.Fixture!=s.Requests[0].Fixture||p.SourceCodeSha!=s.Requests[0].SourceCodeSha)
                throw new InvalidDataException("PILOT_REQUEST_OUTSIDE_EXACT_SCOPE");
            ValidateEndpoint(p);
        }
        if(!s.Instruments.Order().SequenceEqual(s.Requests.Select(p=>p.Instrument).Distinct().Order())||
            !s.Datasets.Order().SequenceEqual(s.Requests.Select(p=>p.Dataset).Distinct().Order()))throw new InvalidDataException("PILOT_SCOPE_REQUEST_MISMATCH");
    }
    public static void ValidateEndpoint(CapturePlan p)
    {
        var u=AcquisitionSafety.ValidateUrl(p.SourceUrl);
        // Version 1 deliberately supports price only; T86 is market-wide and would expand an instrument-bound request.
        var expected=p.Provider=="TWSE"?$"https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY?date={p.RequestDate:yyyyMMdd}&stockNo={p.Instrument}&response=json":
            $"https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock?date={p.RequestDate:yyyy/MM/dd}&code={p.Instrument}&response=json";
        if(p.Dataset!="Price"||u.AbsoluteUri!=new Uri(expected).AbsoluteUri)throw new InvalidDataException("PILOT_ENDPOINT_NOT_ALLOWLISTED");
    }
    public static CapturePermission[] Effective(PilotScope s,PilotAuthorization a,PilotTrust t)=>a.Permissions.Where(p=>
        s.RightsDigests.Contains(p.Digest)&&t.PermissionDigests.Contains(p.Digest)&&!t.RevokedDigests.Contains(p.Digest)&&
        !string.IsNullOrWhiteSpace(p.Reviewer)&&!string.Equals(p.Reviewer,s.RequestedBy,StringComparison.OrdinalIgnoreCase)&&Digest(p.EvidenceReference)).ToArray();
    public static string[] Blockers(PilotScope s,PilotAuthorization a,PilotTrust t,DateTimeOffset now,params string[] purposes)
    {
        Validate(s);var reasons=new List<string>();
        if(now.Offset!=TimeSpan.Zero||now<s.ForwardStart||now>=s.ForwardEnd||now>=s.ExpiresAt)reasons.Add("PILOT_EXPIRED_OR_NOT_STARTED");
        if(!t.Isolated||string.IsNullOrWhiteSpace(t.Environment))reasons.Add("AUTHORIZED_ISOLATED_ENVIRONMENT_REQUIRED");
        if(!a.Approvals.Any(p=>p.ScopeId==s.Id&&p.Environment==t.Environment&&p.RequestedBy==s.RequestedBy&&
            !string.IsNullOrWhiteSpace(p.Reviewer)&&!string.Equals(p.Reviewer,s.RequestedBy,StringComparison.OrdinalIgnoreCase)&&p.Status=="APPROVED"&&p.ValidFrom.Offset==TimeSpan.Zero&&p.ExpiresAt.Offset==TimeSpan.Zero&&
            p.ValidFrom<=now&&p.ExpiresAt>now&&p.ExpiresAt<=s.ExpiresAt&&Digest(p.EvidenceDigest)&&
            p.Digest==ResearchMath.Digest(p with{Digest=""})&&t.ApprovalDigests.Contains(p.Digest)&&!t.RevokedDigests.Contains(p.Digest)))reasons.Add("INDEPENDENT_SCOPE_AND_ENVIRONMENT_APPROVAL_REQUIRED");
        var rights=Effective(s,a,t);
        foreach(var plan in s.Requests)foreach(var purpose in purposes)
        {
            if(purpose is not ("ACQUIRE" or "STORE" or "RESEARCH" or "REDISTRIBUTE"))throw new ArgumentException("RIGHT_PURPOSE_INVALID");
            if(!rights.Any(p=>p.PlanId==plan.Id&&p.Purpose==purpose&&p.Status=="GRANTED"&&p.ValidFrom.Offset==TimeSpan.Zero&&p.ExpiresAt.Offset==TimeSpan.Zero&&
                p.ValidFrom<=now&&p.ExpiresAt>now&&p.RetentionDays>=plan.MinimumRetentionDays&&p.Digest==ResearchMath.Digest(p with{Digest=""})))
                reasons.Add(purpose+"_INDEPENDENT_SCOPED_RIGHT_REQUIRED:"+plan.Id);
        }
        return reasons.Distinct().ToArray();
    }
    public static void Require(PilotScope s,PilotAuthorization a,PilotTrust t,DateTimeOffset now,params string[] purposes)
    {if(Blockers(s,a,t,now,purposes).Length!=0)throw new InvalidOperationException("PILOT_AUTHORIZATION_BLOCKED");}
}
