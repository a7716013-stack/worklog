using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

public sealed class RealDataApproval(string evidenceRoot,string authorityRoot)
{
    public static readonly IReadOnlyDictionary<EvidenceDomain,string[]> RequiredClaims=new Dictionary<EvidenceDomain,string[]>
    {
        [EvidenceDomain.Availability]=["HistoricalPublication","RevisionBehavior","SourceIdentity"],
        [EvidenceDomain.Universe]=["HistoricalMembership","Listing","Delisting","Suspension","SymbolHistory","Denominator"],
        [EvidenceDomain.Benchmark]=["BenchmarkDefinition","HistoricalMapping","BenchmarkPriceBasis"],
        [EvidenceDomain.CorporateAction]=["PriceBasis","CorporateActionHandling"],
        [EvidenceDomain.Calendar]=["HistoricalSessions","EmergencyClosures","Horizon:TradingSessions"]
    };
    // Candidate-v1 requires explicit historical membership for every component, including single-stock features.
    public static EvidenceDomain[] Dependencies(EvidenceCode code)=>code==EvidenceCode.RelativeStrength
        ? Enum.GetValues<EvidenceDomain>()
        : [EvidenceDomain.Availability,EvidenceDomain.Universe,EvidenceDomain.CorporateAction,EvidenceDomain.Calendar];
    public static string[] FactDependencies(EvidenceCode code)=>code switch
    {
        EvidenceCode.Institutional=>["Price","Institutional"],
        EvidenceCode.RelativeStrength=>["Price","Benchmark"],
        _=>["Price"]
    };
    public static string Subject(HistoricalBuildRequest d,EvidenceDomain domain)=>domain switch
    {
        EvidenceDomain.Availability=>ResearchMath.Digest(new{d.Policies,d.Facts}),
        EvidenceDomain.Universe=>ResearchMath.Digest(new{d.Instruments,d.Universes}),
        EvidenceDomain.Benchmark=>ResearchMath.Digest(d.Benchmarks),
        EvidenceDomain.CorporateAction=>ResearchMath.Digest(d.PricePolicy),
        EvidenceDomain.Calendar=>ResearchMath.Digest(d.Calendar),
        _=>throw new ArgumentException("Unknown domain")
    };
    public static string InputDigest(RealDatasetSubmission s)=>ResearchMath.Digest(s with{Review=null});
    private bool BindingValid(RealDatasetSubmission s,DomainAuthorityBinding b,DateOnly start,DateOnly end)
    {
        try
        {
            var a=new AuthorityStore(authorityRoot).Read(b.AuthorityId);
            var e=new EvidenceAcquisitionStore(evidenceRoot);var review=e.ReadReview(b.ReviewDigest);var raw=e.ReadArtifact(b.EvidenceId);
            var declared=s.Source.Authorities.Single(x=>x.AuthorityId==b.AuthorityId);
            var archived=new AuthorityStore(authorityRoot).ReadEvidence(b.EvidenceId);
            return a.Digest==b.AuthorityRevisionDigest&&a.Authority.Status==AuthorityStatus.Approved&&
                ResearchMath.Digest(a.Authority)==ResearchMath.Digest(declared)&&a.Authority.EvidenceReferences.Contains(b.EvidenceId)&&
                archived.EvidenceDigest==raw.SHA256&&archived.VerificationStatus==EvidenceVerificationStatus.Verified&&
                a.Authority.SourceIdentity==raw.SourceReference&&a.Authority.Provider==raw.Provider&&raw.Domain==b.Domain&&
                review.EvidenceId==b.EvidenceId&&review.ReviewedAt<=a.Authority.ReviewedAt&&a.Authority.ReviewedAt<=s.Source.EvaluatedAt&&
                b.SubjectDigest==Subject(s.Source.Dataset,b.Domain)&&b.CoverageStart<=start&&b.CoverageEnd>=end&&
                review.EffectiveFrom<=b.CoverageStart&&review.EffectiveTo>=b.CoverageEnd&&
                a.Authority.EffectiveFrom<=b.CoverageStart&&(!a.Authority.EffectiveTo.HasValue||a.Authority.EffectiveTo>=b.CoverageEnd)&&
                RequiredClaims[b.Domain].All(c=>b.RequiredClaims.Contains(c)&&review.Supported.Contains(c))&&
                !RequiredClaims[b.Domain].Any(c=>review.Unsupported.Contains(c)||review.Ambiguous.Contains(c));
        }
        catch(Exception ex) when(ex is IOException or ArgumentException or InvalidOperationException or InvalidDataException){return false;}
    }
    public FirstApprovedRealPitDatasetReport Evaluate(RealDatasetSubmission s)
    {
        if(s.RequestedComponents.Length==0||s.RequestedComponents.Distinct().Count()!=s.RequestedComponents.Length||s.RequestedComponents.Any(c=>!Enum.IsDefined(c)))
            throw new ArgumentException("Explicit unique component scope required.");
        var d=s.Source.Dataset;var data=new HistoricalDatasetBuilder().Build(d);var digest=InputDigest(s);
        var start=d.Dates.Min(x=>x.TradeDate);var end=d.Dates.Max(x=>x.TradeDate);
        // Authority scope includes feature lookback and label sessions, not just the scored dates.
        var scopeStart=d.Calendar.Sessions.IsEmpty?start:d.Calendar.Sessions.Min();
        var scopeEnd=d.Calendar.Sessions.IsEmpty?end:d.Calendar.Sessions.Max();
        var validBindings=s.DomainBindings.Where(b=>BindingValid(s,b,scopeStart,scopeEnd)).ToArray();
        var domains=Enum.GetValues<EvidenceDomain>().Select(domain=>
        {
            var bindings=s.DomainBindings.Where(b=>b.Domain==domain).ToArray();
            var valid=validBindings.Any(b=>b.Domain==domain);
            return new EvidenceDomainStatus(domain,valid?"VERIFIED":"UNPROVEN",valid?"APPROVED":"BLOCKED",
                valid?"REVIEWED_REQUESTED_PERIOD":"UNKNOWN",valid?[]:[domain+"_EVIDENCE_OR_SCOPE_GAP"]);
        }).ToArray();
        var sourceCurrent=true;
        try
        {
            var registry=new AuthorityStore(authorityRoot);
            foreach(var a in s.Source.Authorities)if(ResearchMath.Digest(registry.Read(a.AuthorityId).Authority)!=ResearchMath.Digest(a))sourceCurrent=false;
            foreach(var e in s.Source.Evidence)if(ResearchMath.Digest(registry.ReadEvidence(e.EvidenceId))!=ResearchMath.Digest(e))sourceCurrent=false;
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or InvalidOperationException){sourceCurrent=false;}
        var review=s.Review;
        var reviewed=review is not null&&review.Status==AuthorityStatus.Approved&&review.SubmissionDigest==digest&&
            review.ReviewedAt>=d.CreatedAt&&review.ReviewedAt<=s.Source.EvaluatedAt&&
            s.Source.Authorities.All(a=>a.ReviewedAt<=review.ReviewedAt)&&!string.IsNullOrWhiteSpace(review.ReviewedBy)&&!string.IsNullOrWhiteSpace(review.ReasonCode);
        var ready=Enum.GetValues<EvidenceCode>().Select(code=>
        {
            var dependencies=Dependencies(code);var facts=FactDependencies(code);
            var reasons=new List<string>();
            foreach(var domain in dependencies)if(domains.Single(x=>x.Domain==domain).AuthorityStatus!="APPROVED")reasons.Add(domain+"_AUTHORITY_GAP");
            var required=d.Facts.Where(f=>facts.Contains(f.DatasetType.ToString())&&data.Decisions.Any(x=>x.FactChecksum==f.FactChecksum&&x.Eligible)).ToArray();
            if(required.Length==0||facts.Any(kind=>!required.Any(f=>f.DatasetType.ToString()==kind)))reasons.Add("REQUIRED_FACTS_MISSING");
            if(required.Any(f=>!HistoricalAuthority.Authorize(s.Source,f).Authorized))reasons.Add("FACT_AUTHORITY_GAP");
            if(!sourceCurrent)reasons.Add("REGISTRY_CHANGED_OR_MISSING");
            if(data.Snapshots.Length!=d.Dates.Length||data.Snapshots.Length==0)reasons.Add("SNAPSHOT_COVERAGE_GAP");
            if(d.Archives.Length==0||d.Archives.Any(a=>a.Synthetic))reasons.Add("REAL_SOURCE_REQUIRED");
            if(d.Universes.Any(u=>!u.IncludesDelistedHistory))reasons.Add("SURVIVORSHIP_BIAS_RISK");
            var coverage=data.Quality.Coverage.Where(c=>facts.Any(f=>c.Domain.Contains(f,StringComparison.OrdinalIgnoreCase))).ToArray();
            if(coverage.Length==0||coverage.Any(c=>c.Coverage!=1))reasons.Add("TEMPORAL_COVERAGE_GAP");
            if(!reviewed)reasons.Add("DATASET_REVIEW_REQUIRED");
            var missing=dependencies.Where(domain=>domains.Single(x=>x.Domain==domain).AuthorityStatus!="APPROVED").Select(x=>x.ToString()).ToArray();
            return new EvidenceReadinessReport(code,"LOCKED_CANDIDATE_V1",dependencies.Select(x=>x.ToString()).Concat(facts).ToArray(),
                validBindings.Where(b=>dependencies.Contains(b.Domain)).Select(b=>b.AuthorityId).Distinct().Order().ToArray(),
                missing,coverage.Length==0?null:coverage.Min(c=>c.Coverage),reasons.Count==0,reasons.Distinct().ToArray());
        }).ToArray();
        var receipts=ready.Where(r=>r.ResearchReady&&s.RequestedComponents.Contains(r.Evidence)).Select(r=>
        {
            var receipt=new ComponentDatasetApprovalReceipt(r.Evidence,data.DatasetSnapshotId,r.ApprovedAuthorities,
                data.Snapshots.Select(x=>x.Data.Identity.UniverseSnapshotId).Distinct().Order().ToArray(),
                r.Evidence==EvidenceCode.RelativeStrength?data.Snapshots.Select(x=>x.Data.Identity.BenchmarkVersionId).Distinct().Order().ToArray():[],
                d.Calendar.Version,d.PricePolicy.PriceBasisVersion,start,end,digest,"",review!.ReviewedAt);
            return receipt with{ApprovalDigest=ResearchMath.Digest(receipt)};
        }).ToArray();
        FullModelDatasetApprovalReceipt? full=null;
        if(receipts.Length==6)
        {
            full=new(new Guid(Convert.FromHexString(digest)[..16]),data.DatasetSnapshotId,receipts,100,start,end,digest,"",review!.ReviewedAt);
            full=full with{ApprovalDigest=ResearchMath.Digest(full)};
        }
        var report=new FirstApprovedRealPitDatasetReport(full is not null?"FIRST_APPROVED_REAL_PIT_DATASET":receipts.Length>0?"COMPONENT_DATASET_APPROVED":"BLOCKED_BY_AUTHORITY_OR_DATA",
            full?.DatasetApprovalId,start,end,data.Snapshots.Length,data.Quality.InstrumentCount,
            data.Snapshots.Sum(x=>x.Data.UniverseMembers.Length),domains,ready,receipts,full,ready.SelectMany(x=>x.ReasonCodes).Distinct().ToArray(),digest,"");
        return report with{ApprovalDigest=ResearchMath.Digest(report)};
    }
    public static UniverseCoverageReport ReconstructUniverse(DateOnly date,DateTimeOffset cutoff,int? expected,UniverseIdentityFact[] facts)
    {
        if(expected<0)throw new ArgumentException("Negative denominator.");
        var visible=facts.Where(f=>f.AvailableAt.HasValue&&f.AvailableAt<=cutoff&&f.AuthorityId!=Guid.Empty&&!string.IsNullOrWhiteSpace(f.SourceFactDigest)).ToArray();
        var listed=visible.Where(f=>f.EventType=="Listing"&&f.ValidFrom<=date&&(!f.ValidTo.HasValue||f.ValidTo>=date)).ToArray();
        var excluded=visible.Where(f=>(f.EventType=="Delisting"||f.EventType=="Suspension")&&f.ValidFrom<=date&&(!f.ValidTo.HasValue||f.ValidTo>=date)).Select(f=>f.InstrumentId).ToHashSet();
        var members=listed.Where(f=>f.InstrumentId!=Guid.Empty&&!excluded.Contains(f.InstrumentId)).Select(f=>f.InstrumentId).Distinct().Order().ToArray();
        var unknown=facts.Count(f=>f.InstrumentId==Guid.Empty||f.AvailableAt is null||f.AuthorityId==Guid.Empty||string.IsNullOrWhiteSpace(f.SourceFactDigest));
        var gaps=new List<string>();if(expected is null)gaps.Add("EXPECTED_COUNT_UNKNOWN");if(expected!=members.Length)gaps.Add("MEMBERSHIP_COVERAGE_GAP");if(unknown>0)gaps.Add("IDENTITY_OR_PROVENANCE_GAP");
        return new(date,expected,members.Length,expected is null?null:Math.Max(0,expected.Value-members.Length),members.Length,
            members.Count(id=>visible.Any(f=>f.InstrumentId==id&&f.EventType=="Delisting"&&f.ValidFrom>date)),unknown,
            gaps.Count==0?"COMPLETE":"UNKNOWN_OR_PARTIAL",gaps.ToArray(),members,ResearchMath.Digest(members));
    }
    public static bool PriceCompatible(string stock,string benchmark,PriceBasisCompatibility rule)=>
        rule.Compatible&&rule.StockBasisVersion==stock&&rule.BenchmarkBasisVersion==benchmark&&
        !string.IsNullOrWhiteSpace(rule.Version)&&rule.ReviewDigest.Length==64&&rule.ReviewDigest.All(Uri.IsHexDigit);
}
