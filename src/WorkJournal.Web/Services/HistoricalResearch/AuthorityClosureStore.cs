using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

// Claim-level operator attestations, additional to (never replacing) the Phase 4D/E approval gates.
public sealed class AuthorityClosureStore(string root,string evidenceRoot)
{
    private string DirectoryFor(Guid id)=>Path.Combine(root,"cases",id.ToString("N"));
    public static EvidenceDomain EvidenceDomainFor(ClosureDomain domain)=>domain switch
    {
        ClosureDomain.TradingCalendar=>EvidenceDomain.Calendar,
        ClosureDomain.HistoricalInstrumentIdentity or ClosureDomain.HistoricalUniverse=>EvidenceDomain.Universe,
        ClosureDomain.HistoricalPriceVolume or ClosureDomain.Availability=>EvidenceDomain.Availability,
        ClosureDomain.CorporateActionPriceBasis=>EvidenceDomain.CorporateAction,
        _=>throw new ArgumentException("Unknown closure domain.")
    };
    public static string[] RequiredClaims(ClosureDomain domain,bool needsVolume)=>domain switch
    {
        ClosureDomain.TradingCalendar=>["HistoricalSessions","ScheduledHolidays","EmergencyClosures","Horizon:TradingSessions"],
        ClosureDomain.HistoricalInstrumentIdentity=>["HistoricalInstrumentIdentity","SymbolHistory","MarketTransfer"],
        ClosureDomain.HistoricalUniverse=>["HistoricalMembership","Listing","Delisting","Suspension","Denominator"],
        ClosureDomain.HistoricalPriceVolume=>needsVolume?["HistoricalPrices","HistoricalVolumes","Units","SourceIdentity"]:["HistoricalPrices","Units","SourceIdentity"],
        ClosureDomain.Availability=>["HistoricalPublication","RevisionBehavior","SourceIdentity"],
        ClosureDomain.CorporateActionPriceBasis=>["PriceBasis","CorporateActionHandling"],
        _=>throw new ArgumentException("Unknown closure domain.")
    };
    public static string Subject(HistoricalBuildRequest d,ClosureDomain domain)=>domain switch
    {
        ClosureDomain.TradingCalendar=>ResearchMath.Digest(d.Calendar),
        ClosureDomain.HistoricalInstrumentIdentity=>ResearchMath.Digest(d.Instruments),
        ClosureDomain.HistoricalUniverse=>ResearchMath.Digest(d.Universes),
        ClosureDomain.HistoricalPriceVolume=>ResearchMath.Digest(new{Facts=d.Facts.Where(f=>f.DatasetType==HistoricalDatasetType.Price).ToArray(),Archives=d.Archives.Where(a=>a.DatasetType==HistoricalDatasetType.Price).ToArray()}),
        ClosureDomain.Availability=>ResearchMath.Digest(new{Policies=d.Policies.Where(p=>p.DatasetType==HistoricalDatasetType.Price).ToArray(),Facts=d.Facts.Where(f=>f.DatasetType==HistoricalDatasetType.Price).ToArray()}),
        ClosureDomain.CorporateActionPriceBasis=>ResearchMath.Digest(d.PricePolicy),
        _=>throw new ArgumentException("Unknown closure domain.")
    };
    public ClosureRevision Open(AuthorityClosureCase c)
    {
        if(c.CaseId==Guid.Empty||!Enum.IsDefined(c.Domain)||c.CurrentStatus!=ClosureStatus.OPEN||c.TargetStart>c.TargetEnd||
            c.RequiredClaims.Length==0||c.RequiredClaims.Distinct(StringComparer.Ordinal).Count()!=c.RequiredClaims.Length||
            c.RequiredClaims.Concat(new[]{c.Provider,c.DatasetType}).Any(string.IsNullOrWhiteSpace)||c.EvidenceIds.Length!=0||c.ReviewIds.Length!=0||
            c.LastReviewedAt is not null||c.ClosedAt is not null||c.ClosureDigest!=""||c.BlockingReasons.Length!=0)
            throw new ArgumentException("New immutable case definition must start OPEN without evidence, reviews or closure.");
        EvidenceAcquisitionStore.RequireDigest(c.SubjectDigest);
        c=c with{RequiredClaims=c.RequiredClaims.Order(StringComparer.Ordinal).ToArray()};
        var claims=c.RequiredClaims.Select(claim=>new ClosureClaimDecision(claim,"UNPROVEN",[],[],["NO_SCOPED_SUPPORT"])).ToArray();
        c=c with{ClosureDigest=ResearchMath.Digest(c)};
        var revision=new ClosureRevision(0,"",c,[],claims,c.OpenedAt,"operator","OPEN","");
        return Append(revision);
    }
    private ClosureRevision Append(ClosureRevision r)
    {
        r=r with{Digest=ResearchMath.Digest(r with{Digest=""})};var directory=DirectoryFor(r.Case.CaseId);Directory.CreateDirectory(directory);
        HistoricalDatasetStore.WriteNew(Path.Combine(directory,r.Sequence.ToString("D8",System.Globalization.CultureInfo.InvariantCulture)+".json"),r);return r;
    }
    public ClosureRevision Read(Guid id)
    {
        ClosureRevision? prior=null;var files=Directory.GetFiles(DirectoryFor(id),"*.json").Order(StringComparer.Ordinal).ToArray();
        foreach(var file in files)
        {
            var current=HistoricalDatasetStore.Read<ClosureRevision>(file);
            if(current.Case.CaseId!=id||current.Sequence!=(prior?.Sequence+1??0)||current.PreviousDigest!=(prior?.Digest??"")||
                current.Digest!=ResearchMath.Digest(current with{Digest=""})||current.Case.ClosureDigest!=ResearchMath.Digest(current.Case with{ClosureDigest=""}))
                throw new InvalidDataException("Closure sequence or digest mismatch.");
            if(prior is null)
            {
                var c=current.Case;
                if(c.CurrentStatus!=ClosureStatus.OPEN||c.EvidenceIds.Length!=0||c.ReviewIds.Length!=0||current.ReviewDigests.Length!=0||
                    c.LastReviewedAt is not null||c.ClosedAt is not null||c.OpenedAt!=current.At)
                    throw new InvalidDataException("Closure history must start OPEN.");
            }
            else
            {
                var request=new ClosureReviewRequest(id,prior.Digest,current.Case.CurrentStatus,
                    current.Case.EvidenceIds.Except(prior.Case.EvidenceIds).ToArray(),current.ReviewDigests.Except(prior.ReviewDigests).ToArray(),current.At,current.Reviewer,current.Reason);
                var expected=Next(prior,request);
                if(ResearchMath.Digest(expected with{Digest=""})!=ResearchMath.Digest(current with{Digest=""}))throw new InvalidDataException("Closure review replay mismatch.");
            }
            prior=current;
        }
        return prior??throw new InvalidDataException("Empty closure history.");
    }
    public ClosureRevision Review(ClosureReviewRequest request)=>Append(Next(Read(request.CaseId),request));
    private ClosureRevision Next(ClosureRevision old,ClosureReviewRequest request)
    {
        if(old.Digest!=request.ExpectedRevisionDigest||old.Case.CaseId!=request.CaseId||!Allowed(old.Case.CurrentStatus,request.NextStatus)||
            request.At<old.At||new[]{request.Reviewer,request.Reason}.Any(string.IsNullOrWhiteSpace))throw new InvalidOperationException("Stale or invalid closure review transition.");
        var c=old.Case;
        var ids=c.EvidenceIds.Concat(request.AddEvidenceIds).Distinct().Order().ToArray();
        var digests=old.ReviewDigests.Concat(request.AddReviewDigests).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if(request.NextStatus==ClosureStatus.SUPERSEDED&&(request.AddEvidenceIds.Length>0||request.AddReviewDigests.Length>0))throw new InvalidOperationException("Supersession cannot rewrite evidence.");
        var store=new EvidenceAcquisitionStore(evidenceRoot);
        var artifacts=ids.Select(store.ReadArtifact).ToArray();var reviews=digests.Select(store.ReadReview).ToArray();
        if(artifacts.Any(a=>a.Provider!=c.Provider||a.DatasetType!=c.DatasetType||a.Domain!=EvidenceDomainFor(c.Domain)||a.CapturedAt>request.At)||
            reviews.Any(r=>!ids.Contains(r.EvidenceId)||r.ReviewedAt>request.At)||reviews.Select(r=>r.ReviewId).Distinct().Count()!=reviews.Length)
            throw new InvalidOperationException("Evidence/review provider, domain, dataset, identity or time mismatch.");
        if(request.NextStatus is ClosureStatus.EVIDENCE_CAPTURED or ClosureStatus.UNDER_REVIEW&&ids.Length==0)throw new InvalidOperationException("Captured evidence required.");
        var decisions=c.RequiredClaims.Select(claim=>
        {
            var applicable=reviews.Where(r=>r.EffectiveFrom<=c.TargetStart&&r.EffectiveTo>=c.TargetEnd).ToArray();
            var support=applicable.Where(r=>r.Supported.Contains(claim)).ToArray();
            var negative=applicable.Where(r=>r.Unsupported.Contains(claim)||r.Ambiguous.Contains(claim)).ToArray();
            var status=negative.Length>0?(support.Length>0?"CONFLICTING":"UNSUPPORTED_OR_AMBIGUOUS"):support.Length>0?"SUPPORTED":"UNPROVEN";
            var contributing=support.Concat(negative).ToArray();
            return new ClosureClaimDecision(claim,status,contributing.Select(r=>r.EvidenceId).Distinct().Order().ToArray(),
                contributing.Select(r=>r.ReviewId).Distinct().Order().ToArray(),status=="SUPPORTED"?[]:[status=="UNPROVEN"?"NO_SCOPED_SUPPORT":"REVIEW_CONFLICT_OR_UNSUPPORTED"]);
        }).ToArray();
        var supported=decisions.Count(d=>d.Status=="SUPPORTED");
        if(request.NextStatus==ClosureStatus.APPROVED&&supported!=decisions.Length)throw new InvalidOperationException("All required claims need scoped, non-conflicting support.");
        if(request.NextStatus==ClosureStatus.PARTIALLY_SUPPORTED&&(supported==0||supported==decisions.Length))throw new InvalidOperationException("Partial support requires both supported and unresolved claims.");
        if(request.NextStatus==ClosureStatus.BLOCKED&&supported==decisions.Length)throw new InvalidOperationException("No blocking claim remains.");
        c=c with{EvidenceIds=ids,ReviewIds=reviews.Select(r=>r.ReviewId).Order().ToArray(),CurrentStatus=request.NextStatus,
            BlockingReasons=decisions.Where(d=>d.Status!="SUPPORTED").Select(d=>d.Claim+":"+d.Status).ToArray(),LastReviewedAt=request.At,
            ClosedAt=request.NextStatus is ClosureStatus.APPROVED or ClosureStatus.REJECTED or ClosureStatus.SUPERSEDED?request.At:null,ClosureDigest=""};
        c=c with{ClosureDigest=ResearchMath.Digest(c)};
        return new(old.Sequence+1,old.Digest,c,digests,decisions,request.At,request.Reviewer,request.Reason,"");
    }
    private static bool Allowed(ClosureStatus from,ClosureStatus to)=>(from,to) switch
    {
        (ClosureStatus.OPEN,ClosureStatus.EVIDENCE_CAPTURED or ClosureStatus.BLOCKED)=>true,
        (ClosureStatus.EVIDENCE_CAPTURED,ClosureStatus.UNDER_REVIEW)=>true,
        (ClosureStatus.UNDER_REVIEW,ClosureStatus.PARTIALLY_SUPPORTED or ClosureStatus.APPROVED or ClosureStatus.REJECTED or ClosureStatus.BLOCKED)=>true,
        (ClosureStatus.PARTIALLY_SUPPORTED or ClosureStatus.BLOCKED,ClosureStatus.EVIDENCE_CAPTURED or ClosureStatus.UNDER_REVIEW)=>true,
        (ClosureStatus.APPROVED,ClosureStatus.SUPERSEDED)=>true,
        _=>false
    };
    public ComponentClosureReport Evaluate(ComponentClosureRequest request)
    {
        var components=request.Dataset.RequestedComponents;
        if(components.Length==0||components.Distinct().Count()!=components.Length||components.Any(c=>c is not EvidenceCode.Trend and not EvidenceCode.Momentum and not EvidenceCode.VolumePrice))
            throw new ArgumentException("Phase 4F first scope is explicitly Trend, Momentum and/or VolumePrice.");
        var d=request.Dataset.Source.Dataset;var reasons=new List<string>();var cases=new List<ClosureRevision>();
        var start=d.Calendar.Sessions.IsEmpty?d.Dates.Min(x=>x.TradeDate):d.Calendar.Sessions.Min();
        var end=d.Calendar.Sessions.IsEmpty?d.Dates.Max(x=>x.TradeDate):d.Calendar.Sessions.Max();
        foreach(var domain in Enum.GetValues<ClosureDomain>())
        {
            var links=request.Closures.Where(b=>b.Domain==domain).ToArray();
            if(links.Length!=1){reasons.Add(domain+":MISSING_OR_DUPLICATE_CASE");continue;}
            try
            {
                var current=Read(links[0].CaseId);cases.Add(current);var c=current.Case;
                if(current.Digest!=links[0].RevisionDigest||c.CurrentStatus!=ClosureStatus.APPROVED||c.Domain!=domain||c.SubjectDigest!=Subject(d,domain)||
                    c.TargetStart>start||c.TargetEnd<end||c.ClosedAt>request.Dataset.Review?.ReviewedAt||request.Dataset.Review is null||
                    !RequiredClaims(domain,components.Contains(EvidenceCode.VolumePrice)).All(claim=>current.Claims.Any(x=>x.Claim==claim&&x.Status=="SUPPORTED")))
                    reasons.Add(domain+":NOT_CURRENT_APPROVED_SCOPED_CLAIMS");
            }
            catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException){reasons.Add(domain+":INVALID_ARCHIVE");}
        }
        var report=new ComponentClosureReport(reasons.Count==0?"CLOSURE_READY_REQUIRES_DATASET_APPROVAL":"BLOCKED_BY_AUTHORITY_OR_DATA",
            components,Enum.GetValues<ClosureDomain>(),cases.ToArray(),reasons.ToArray(),ResearchMath.Digest(request),RealDataApproval.InputDigest(request.Dataset),"");
        return report with{ReportDigest=ResearchMath.Digest(report)};
    }
}
