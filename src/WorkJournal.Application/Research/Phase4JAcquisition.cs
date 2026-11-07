using System.Net;
using System.Net.Sockets;
using WorkJournal.Web.Models.HistoricalResearch.Resolution;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record AcquisitionReservation(string TaskId,string Provider,DateTimeOffset At,string SourceVersion);
public static class AcquisitionSafety
{
    public const int MaximumAttempts=3,ProviderHourlyLimit=20,TimeoutSeconds=30,MaximumBytes=16*1024*1024,Concurrency=1;
    public static void RequireCurrentSources(string root)
    {
        var dir=Path.Combine(root,"supply-chain");if(!Directory.Exists(dir))return;
        if(Directory.GetFiles(dir,"*.impact.json").Length>0||Directory.GetFiles(dir,"*.result.json").Select(HistoricalDatasetStore.Read<AcquisitionAttemptView>).Any(a=>a.RawDigest is not null&&a.RawDigest!=a.SourceVersion))
            throw new InvalidOperationException("SOURCE_VERSION_INVALIDATED_REEVALUATION_REQUIRED");
    }
    public static Uri ValidateUrl(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var u)||u.Scheme!="https"||u.Port!=443||u.UserInfo.Length!=0||u.Fragment.Length!=0||
            u.Host is not ("www.twse.com.tw" or "wwwc.twse.com.tw" or "openapi.twse.com.tw" or "www.tpex.org.tw"))
            throw new ArgumentException("UNREGISTERED_OR_UNSAFE_SOURCE");
        return u;
    }
    public static bool PublicAddress(IPAddress ip)
    {
        if(ip.IsIPv4MappedToIPv6)ip=ip.MapToIPv4();var b=ip.GetAddressBytes();
        if(ip.AddressFamily==AddressFamily.InterNetworkV6)return b[0]>=0x20&&b[0]<=0x3f&&!(b[0]==0x20&&b[1]==0x01&&b[2]==0x0d&&b[3]==0xb8);
        return b[0] is >0 and <224&&b[0]!=10&&b[0]!=127&&!(b[0]==169&&b[1]==254)&&!(b[0]==172&&b[1]>=16&&b[1]<=31)&&
            !(b[0]==192&&(b[1]==168||b[1]==0||b[1]==2))&&!(b[0]==100&&b[1]>=64&&b[1]<=127)&&!(b[0]==198&&b[1] is 18 or 19)&&
            !(b[0]==198&&b[1]==51&&b[2]==100)&&!(b[0]==203&&b[1]==0&&b[2]==113);
    }
    public static string Classify(string? status)=>status switch
    {
        "HTTP_SUCCESS"=>"CAPTURED_REVIEW_REQUIRED",
        "HTTP_404" or "HTTP_410"=>"SOURCE_UNAVAILABLE",
        "HTTP_401" or "HTTP_403"=>"MANUAL_ACTION_REQUIRED",
        "HTTP_429"=>"PROVIDER_QUOTA",
        "HTTP_408" or "TaskCanceledException" or "OperationCanceledException"=>"PROVIDER_TIMEOUT",
        "HTTP_500" or "HTTP_502" or "HTTP_503" or "HTTP_504" or "HttpRequestException"=>"TRANSIENT_PROVIDER_FAILURE",
        _=>"SOURCE_FAILURE_UNPROVEN_DATA_EXISTENCE"
    };
    public static bool Retryable(string status)=>status is "PROVIDER_TIMEOUT" or "TRANSIENT_PROVIDER_FAILURE";
    public static void CheckBudget(AcquisitionReservation[] records,AcquisitionWork task,string provider,DateTimeOffset now,int historicalAttempts=0)
    {
        if(historicalAttempts<0)throw new ArgumentException("Invalid historical attempt count");
        if(historicalAttempts+records.Count(r=>r.TaskId==task.TaskId)>=MaximumAttempts)throw new InvalidOperationException("RETRY_LIMIT");
        if(records.Count(r=>r.Provider==provider&&r.At>now.AddHours(-1))>=ProviderHourlyLimit)throw new InvalidOperationException("PROVIDER_QUOTA");
    }
    // Resolve once, reject private addresses, connect to the checked address; TLS still validates the original host.
    public static SocketsHttpHandler Transport()=>new(){
        AllowAutoRedirect=false,UseCookies=false,UseProxy=false,ConnectTimeout=TimeSpan.FromSeconds(10),
        ConnectCallback=async(context,token)=>{
            var addresses=await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,token);
            if(addresses.Length==0||addresses.Any(a=>!PublicAddress(a)))throw new HttpRequestException("UNSAFE_DNS_ADDRESS");
            var socket=new Socket(addresses[0].AddressFamily,SocketType.Stream,ProtocolType.Tcp);
            try{await socket.ConnectAsync(new IPEndPoint(addresses[0],context.DnsEndPoint.Port),token);return new NetworkStream(socket,true);}
            catch{socket.Dispose();throw;}
        }
    };
    public static HttpClient Client()=>new(Transport()){Timeout=TimeSpan.FromSeconds(TimeoutSeconds)};
    public static FileStream Lease(string dir)=>new(Path.Combine(dir,"execution.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
}

// One filesystem lock across providers/processes. Reservations survive timeout/crash, so retries cannot reset quotas.
public sealed class Phase4JAcquisition(string root,string evidenceRoot)
{
    public async Task<AcquisitionAttemptView[]> Execute(Phase4JReport report,Phase4IReport parent,string taskId,AuthorityResolutionTask legacyTask,string op,CancellationToken token=default)
    {
        Phase4JProjection.Validate(report,parent);
        var task=report.Plan.Single(t=>t.TaskId==taskId);var source=report.Sources.Single(s=>s.SourceId==task.SourceId&&s.SourceVersion==task.SourceVersion);
        AcquisitionSafety.ValidateUrl(source.OfficialUrl);
        if(task.ExecutionMode!="CONTROLLED_CLI")throw new InvalidOperationException("MANUAL_ACTION_REQUIRED");
        if(legacyTask.AuthorityDomain+":"+legacyTask.RequiredClaimId!=task.ClaimKey||legacyTask.TargetStart!=task.CoverageStart||legacyTask.TargetEnd!=task.CoverageEnd)
            throw new InvalidDataException("Legacy task scope mismatch");
        // Replay source before network; a catalog hash alone never substitutes for archived evidence.
        var evidence=new EvidenceAcquisitionStore(evidenceRoot);
        foreach(var id in source.EvidenceIds){var raw=evidence.ReadArtifact(id);if(raw.SourceReference!=source.OfficialUrl||raw.SHA256!=source.SourceVersion)throw new InvalidDataException("SOURCE_VERSION_MISMATCH");}
        var dir=Path.Combine(root,"supply-chain");Directory.CreateDirectory(dir);
        using var gate=AcquisitionSafety.Lease(dir);
        var output=new List<AcquisitionAttemptView>();
        using var client=AcquisitionSafety.Client();var ledger=new AuthorityResolutionLedger(root,evidenceRoot);
        var managed=Directory.GetFiles(dir,"*.result.json").Select(HistoricalDatasetStore.Read<AcquisitionAttemptView>).Select(a=>a.AttemptId).ToHashSet();
        var historical=ledger.Attempts(legacyTask.ResolutionTaskId).Count(a=>!managed.Contains(a.AttemptId)&&a.SourceReference==source.OfficialUrl&&(a.RawDigest is null||a.RawDigest==source.SourceVersion));
        var provider=new Uri(source.OfficialUrl).Host.EndsWith(".twse.com.tw",StringComparison.Ordinal)?"TWSE":"TPEX";
        for(var n=0;n<AcquisitionSafety.MaximumAttempts;n++)
        {
            token.ThrowIfCancellationRequested();var now=DateTimeOffset.UtcNow;
            var reservations=Directory.GetFiles(dir,"*.reservation.json").Select(HistoricalDatasetStore.Read<AcquisitionReservation>).ToArray();
            AcquisitionSafety.CheckBudget(reservations,task,provider,now,historical);
            var ticket=Guid.NewGuid();HistoricalDatasetStore.WriteNew(Path.Combine(dir,ticket+".reservation.json"),new AcquisitionReservation(taskId,provider,now,source.SourceVersion));
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(AcquisitionSafety.TimeoutSeconds));
            var a=await ledger.Acquire(legacyTask,source.OfficialUrl,op,client,TimeProvider.System,deadline.Token);
            var view=new AcquisitionAttemptView(a.AttemptId,taskId,a.RequestedAt,a.ReceivedAt,AcquisitionSafety.Classify(a.AcquisitionStatus),a.RawDigest,a.EvidenceId,source.SourceVersion,a.Digest);
            HistoricalDatasetStore.WriteNew(Path.Combine(dir,ticket+".result.json"),view);output.Add(view);
            if(!AcquisitionSafety.Retryable(view.Status)||n==AcquisitionSafety.MaximumAttempts-1)break;
            await Task.Delay(TimeSpan.FromSeconds(2*(n+1)),token);
        }
        return output.ToArray();
    }
}
