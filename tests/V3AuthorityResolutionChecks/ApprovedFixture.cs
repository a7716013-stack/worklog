using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.HistoricalResearch.Components;
using WorkJournal.Web.Models.HistoricalResearch.Resolution;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;

// Isolated simulated transport/attestations exercise the complete approval path. Never copied to the real evidence store.
internal static class ApprovedFixture
{
    public static async Task<(TrendResolutionRequest Request, ResearchRequest Template, string Evidence, string Authority, string Closure)> Create(string root)
    {
        var fixture=Fixture.Create();var d=fixture.Submission.Dataset;var dates=d.Calendar.Sessions;var at=d.CreatedAt;var before=at.AddYears(-2);
        var id=d.Universes[0].Members[0];const string symbol="2330";
        var original=d.Facts.Where(f=>f.DatasetType==HistoricalDatasetType.Price&&f.SourceInstrumentKey==id.ToString()).OrderBy(f=>f.TradeDate).ToArray();
        var payload=JsonSerializer.Serialize(new{stat="OK",fields=new[]{"日期","成交股數","成交金額","開盤價","最高價","最低價","收盤價"},data=original.Select(f=>new[]{
            $"{f.TradeDate.Year-1911}/{f.TradeDate.Month:D2}/{f.TradeDate.Day:D2}",f.Value.Volume!.Value.ToString(CultureInfo.InvariantCulture),f.Value.TradingValue!.Value.ToString(CultureInfo.InvariantCulture),
            f.Value.Open!.Value.ToString(CultureInfo.InvariantCulture),f.Value.High!.Value.ToString(CultureInfo.InvariantCulture),f.Value.Low!.Value.ToString(CultureInfo.InvariantCulture),f.Value.Close!.Value.ToString(CultureInfo.InvariantCulture)}).ToArray()});
        var er=Path.Combine(root,"evidence");var ar=Path.Combine(root,"authority");var cr=Path.Combine(root,"closure");
        var es=new EvidenceAcquisitionStore(er);var authorities=new AuthorityStore(ar);var closures=new AuthorityClosureStore(cr,er);
        var tasks=Enum.GetValues<ClosureDomain>().Select(domain=>new EvidenceAcquisitionTask(Guid.NewGuid(),"TWSE",domain.ToString(),AuthorityClosureStore.EvidenceDomainFor(domain),dates[0],dates[^1],AvailabilityEvidenceType.OfficialSpecification,AcquisitionStatus.Pending,0,null,"",
            domain==ClosureDomain.HistoricalPriceVolume?"https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY?stockNo=2330&date=20240101&response=json":"https://www.twse.com.tw/SIMULATED/"+domain,null,"")).ToArray();
        using var http=new HttpClient(new Transport(payload));var acquired=await es.Acquire(new(Guid.NewGuid(),"TWSE",dates[0],dates[^1],tasks),http,new Clock(at.AddHours(-1)));
        var raw=acquired.CapturedArtifacts.Single(a=>a.DatasetType=="HistoricalPriceVolume");var bytes=File.ReadAllBytes(Path.Combine(er,"evidence","authority",raw.EvidenceId.ToString("N"),"raw.bin"));
        var archive=new SourceArchive("SIMULATED-price","TWSE",HistoricalDatasetType.Price,raw.SourceReference,at,at,Convert.ToBase64String(bytes),raw.SHA256,ComponentEvidence.ParserVersion,false);
        var policy=d.Policies.Single(p=>p.DatasetType==HistoricalDatasetType.Price) with{Provider="TWSE"};
        var facts=original.Select(f=>{var n=f with{Provider="TWSE",SourceInstrumentKey=symbol,ArchiveId=archive.ArchiveId,SourceChecksum=raw.SHA256};return n with{FactChecksum=HistoricalAvailability.FactChecksum(n)};}).ToImmutableArray();
        d=d with{Archives=[archive],Facts=facts,Policies=[policy],Instruments=[new("TWSE",symbol,id,dates[0],null,before,"SIMULATED historical identity","fixture-id-v1")],
            Universes=d.Universes.Select(u=>{var n=u with{Members=[id]};return n with{Checksum=HistoricalAvailability.UniverseChecksum(n)};}).ToImmutableArray(),Benchmarks=d.Benchmarks.Where(b=>b.InstrumentId==id).ToImmutableArray()};
        var historicalEvidence=new List<HistoricalAvailabilityEvidence>();var declared=new List<HistoricalDataAuthority>();var bindings=new List<DomainAuthorityBinding>();var reviews=new Dictionary<ClosureDomain,EvidenceReviewReceipt>();var links=new List<ComponentClosureBinding>();
        HistoricalDataAuthority Register(EvidenceArtifact a)
        {
            var body=File.ReadAllBytes(Path.Combine(er,"evidence","authority",a.EvidenceId.ToString("N"),"raw.bin"));
            var e=new HistoricalAvailabilityEvidence(a.EvidenceId,"TWSE",HistoricalDatasetType.Price,a.SourceReference,AvailabilityEvidenceType.OfficialSpecification,a.SourceReference,a.CapturedAt,a.SHA256,policy.Version,dates[0],dates[^1],"UTC",EvidenceVerificationStatus.Verified,"SIMULATED",Convert.ToBase64String(body));
            authorities.Evidence(e);historicalEvidence.Add(e);
            var auth=new HistoricalDataAuthority(Guid.NewGuid(),"TWSE",HistoricalDatasetType.Price,a.SourceReference,policy.Version,"fixture-revision-v1",d.Calendar.Version,d.PricePolicy.PriceBasisVersion,dates[0],dates[^1],[e.EvidenceId],null,"",AuthorityStatus.Draft,"SIMULATED",HistoricalAuthority.PolicyScopeDigest(d,policy));
            var rev=authorities.Register(auth);foreach(var status in new[]{AuthorityStatus.EvidenceCollected,AuthorityStatus.UnderReview,AuthorityStatus.Approved})rev=authorities.Review(auth.AuthorityId,rev.Digest,status,at,"SIMULATED reviewer","SIMULATED");
            declared.Add(rev.Authority);return rev.Authority;
        }
        foreach(var domain in Enum.GetValues<ClosureDomain>())
        {
            var a=acquired.CapturedArtifacts.Single(a=>a.DatasetType==domain.ToString());var ed=AuthorityClosureStore.EvidenceDomainFor(domain);
            var required=AuthorityClosureStore.RequiredClaims(domain,false);var all=required.Concat(RealDataApproval.RequiredClaims[ed]).Concat(new[]{"OriginalPublicationTime","Availability:fixture:ExactTimestamp","Revision:IMMUTABLE_SOURCE"}).Distinct().ToArray();
            var review=es.Review(new(Guid.NewGuid(),a.EvidenceId,"SIMULATED reviewer",at,"SIMULATED claim attestation",all,[],[],dates[0],dates[^1],"UTC","SIMULATED; not real proof",a.SHA256,""));reviews[domain]=review;
            var auth=Register(a);
            if(!bindings.Any(b=>b.Domain==ed))bindings.Add(new(ed,RealDataApproval.Subject(d,ed),a.EvidenceId,review.ReviewDigest,auth.AuthorityId,authorities.Read(auth.AuthorityId).Digest,RealDataApproval.RequiredClaims[ed],dates[0],dates[^1]));
            var c=new AuthorityClosureCase(Guid.NewGuid(),domain,"TWSE",domain.ToString(),dates[0],dates[^1],required,[],[],ClosureStatus.OPEN,[],at,null,null,"",AuthorityClosureStore.Subject(d,domain));
            var rev=closures.Open(c);rev=closures.Review(new(c.CaseId,rev.Digest,ClosureStatus.EVIDENCE_CAPTURED,[a.EvidenceId],[],at,"SIMULATED","capture"));rev=closures.Review(new(c.CaseId,rev.Digest,ClosureStatus.UNDER_REVIEW,[],[],at,"SIMULATED","review"));rev=closures.Review(new(c.CaseId,rev.Digest,ClosureStatus.APPROVED,[],[review.ReviewDigest],at,"SIMULATED","approve test claims"));links.Add(new(domain,c.CaseId,rev.Digest));
        }
        var priceAuthority=declared.Single(a=>a.SourceIdentity==raw.SourceReference);
        var source=new AuthoritySubmission(d,declared.ToImmutableArray(),historicalEvidence.ToImmutableArray(),facts.Select(f=>new AuthorityFactBinding(f.FactChecksum,priceAuthority.AuthorityId,"fixture-revision-v1")).ToImmutableArray(),at,null);
        source=source with{Review=new(HistoricalAuthority.SubmissionDigest(source),AuthorityStatus.Approved,at,"SIMULATED","SIMULATED")};
        var submission=new RealDatasetSubmission(source,bindings.ToArray(),[EvidenceCode.Trend],null);submission=submission with{Review=new(RealDataApproval.InputDigest(submission),AuthorityStatus.Approved,at,"SIMULATED","SIMULATED")};
        var request=new ComponentDatasetApprovalRequest(EvidenceCode.Trend,"V3EvidenceCandidate-v1",d.ParameterSetId,d.Dates.Min(x=>x.TradeDate),d.Dates.Max(x=>x.TradeDate),d.Universes[0].DefinitionVersion,d.PricePolicy.PriceBasisVersion,links.Select(l=>l.CaseId).ToArray(),[policy.PolicyId],"SIMULATED",at,"");request=request with{RequestDigest=ResearchMath.Digest(request)};
        Guid Evidence(ClosureDomain domain)=>acquired.CapturedArtifacts.Single(a=>a.DatasetType==domain.ToString()).EvidenceId;
        var snapshots=d.Universes.Select(u=>{var x=new HistoricalUniverseSnapshot(u.SnapshotId,"TWSE",u.AsOfDate,[id],u.DefinitionVersion,[Evidence(ClosureDomain.HistoricalUniverse)],[links.Single(l=>l.Domain==ClosureDomain.HistoricalUniverse).CaseId],at,"");return x with{Digest=ResearchMath.Digest(x)};}).ToArray();
        var dataset=new HistoricalDatasetBuilder().Build(d);var rows=ComponentEvidence.ParsePrice(er,raw.EvidenceId);
        var lineage=facts.Select(f=>{var row=rows.Single(row=>row.Locator.TradeDate==f.TradeDate);return new HistoricalFactLineage(f.FactChecksum,id,f.TradeDate,f.DatasetType,raw.EvidenceId,row.Locator,ComponentEvidence.ParserVersion,row.ParsedValueDigest,f.RevisionId,policy.PolicyId,dataset.DatasetSnapshotId);}).ToArray();
        var avReview=reviews[ClosureDomain.Availability];
        var input=new ComponentDatasetInput(ComponentDataMode.RealApproved,request,new(submission,links.ToArray()),
            dates.Select(date=>new HistoricalTradingSession("TWSE",date,SessionKind.NormalTradingDay,true,new(9,0),new(13,30),before,before,Evidence(ClosureDomain.TradingCalendar),d.Calendar.Version)).ToArray(),
            [new(id,"TWSE",symbol,before,null,dates[0],null,null,null,null,null,before,before,Evidence(ClosureDomain.HistoricalInstrumentIdentity),"original")],snapshots,
            snapshots.Select(u=>new HistoricalUniverseMember(u.UniverseSnapshotId,id,"Active","SIMULATED independently defined universe",true,true,Evidence(ClosureDomain.HistoricalUniverse))).ToArray(),lineage,
            [new("TWSE",HistoricalDatasetType.Price,"fixture-revision-v1",false,"provider revision","publication","",dates[0],dates[^1].AddDays(1),[avReview.EvidenceId],ClaimSupport.SUPPORTED,at,avReview.ReviewDigest)],
            facts.Select(f=>new HistoricalAvailabilityClaim("TWSE",f.DatasetType,f.TradeDate,"publication","UTC",f.AvailableAt,null,[avReview.EvidenceId],AuthorityLevel.OfficialTimestamp,AvailabilityConfidence.Known,policy.Version,f.FactChecksum,avReview.ReviewDigest)).ToArray(),[]);
        var result=AuthorityResolution.Create(input);result=result with{Availability=facts.Select(f=>new ResolutionAvailability(f.FactChecksum,PublicationClaimType.OriginalPublicationTime,new("fixture",AvailabilityPrecision.ExactTimestamp,f.AvailableAt,f.AvailableAt,"ConservativeUpperBound-v1",avReview.ReviewDigest,dates[0],dates[^1]),avReview.EvidenceId,"TWSE",f.DatasetType,policy.Version)).ToArray(),
            Revisions=facts.Select(f=>new ResolutionRevision(f.FactChecksum,RevisionSemantics.IMMUTABLE_SOURCE,"TWSE",f.DatasetType,"fixture-revision-v1",dates[0],dates[^1],f.RevisionId,f.AvailableAt,true,avReview.EvidenceId,avReview.ReviewDigest)).ToArray(),RequestDigest=""};result=result with{RequestDigest=ResearchMath.Digest(result)};
        var outcomes=new OutcomeDataset("SIMULATED-labels","fixture-revision-v1",at,dates,[new(id,d.PricePolicy.PriceBasisVersion,facts.Select(f=>new OutcomeClose(f.TradeDate,f.Value.Close!.Value,f.AvailableAt,EvidenceStatus.Available)).ToImmutableArray())]);
        var protocol=new ResearchProtocol("SIMULATED-protocol",[new("F1",new(dates[130],dates[132]),new(dates[135],dates[137]),new(dates[140],dates[142]))],[1],3,2);
        var template=new ResearchRequest(Guid.NewGuid(),at,"V3EvidenceCandidate-v1","fixture-model","CandidateFeatures-v1",fixture.Parameters,protocol,[],outcomes,[],[],false,[]);
        return(result,template,er,ar,cr);
    }
    sealed class Clock(DateTimeOffset at):TimeProvider{public override DateTimeOffset GetUtcNow()=>at;}
    sealed class Transport(string payload):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=r,Content=new StringContent(r.RequestUri!.AbsolutePath.Contains("STOCK_DAY")?payload:"SIMULATED authority documentation",Encoding.UTF8,"application/json")});}
}
