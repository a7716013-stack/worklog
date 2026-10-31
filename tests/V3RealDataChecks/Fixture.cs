using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;

internal static class Fixture {
 public static (AuthoritySubmission Submission,V3EvidenceCandidateV1ParameterSet Parameters) Create() {
Guid Id(int n)=>Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}");
var dates=Enumerable.Range(0,150).Select(i=>new DateOnly(2024,1,1).AddDays(i)).ToImmutableArray();
DateTimeOffset At(DateOnly d)=>new(d.ToDateTime(new TimeOnly(18,0)),TimeSpan.Zero);
var created=At(dates[^1]).AddDays(1);var before=At(dates[0]).AddDays(-1);
var ids=new[]{Id(1),Id(2),Id(3)};var benchmark=Id(4);
var p=new V3EvidenceCandidateV1ParameterSet{SlopeHorizon=5,StructureHorizon=10,AlignmentBounds=new(-.1m,.1m),PositionBounds=new(-.1m,.1m),SlopeBounds=new(-.1m,.1m),
    RocHorizon=5,MacdBounds=new(-.01m,.01m),RocBounds=new(-.1m,.1m),VolumeHorizon=5,ReturnHorizon=5,BreakoutHorizon=5,RvolLogBounds=new(-1,1),PvcBounds=new(-.1m,.1m),BreakoutBounds=new(-.1m,.1m),
    InstitutionalHorizons=[2,3,5],PersistenceHorizon=5,Persistence=PersistenceGroup.Combined,RelativeStrengthHorizons=[2,3,5],VolatilityHorizon=5,Variance=VarianceDefinition.Population,DrawdownHorizon=5,AtrPeriod=5,Atr=AtrDefinition.SimpleMean,LiquidityHorizon=5,LiquidityPrice="Close"};
var archives=Enum.GetValues<HistoricalDatasetType>().Select(t=>
{
    var bytes=Encoding.UTF8.GetBytes("SIMULATED AUTHORITY FIXTURE ONLY "+t);
    // False deliberately exercises the real-data gate with fabricated test metadata; never external research evidence.
    return new SourceArchive(t.ToString(),"fixture",t,"fixture://authority/"+t,created,created,Convert.ToBase64String(bytes),Convert.ToHexString(SHA256.HashData(bytes)),"fixture-v1",false);
}).ToImmutableArray();
var policies=archives.Select((a,i)=>new FrozenAvailabilityPolicy(Id(10+i),"fixture",a.DatasetType,"fixture-publication-v1",dates[0],null,AvailabilityRule.SourceTimestamp,"UTC",null,"fixture://spec","Simulated original revisions","fixture reviewer",before,before,FoundationStatus.Finalized)).ToImmutableArray();
var facts=new List<HistoricalFact>();
foreach(var a in archives)foreach(var id in a.DatasetType==HistoricalDatasetType.Benchmark?new[]{benchmark}:ids)
foreach(var (date,i) in dates.Select((d,i)=>(d,i)))
{
    var n=Array.IndexOf(ids,id)+1;var price=100+i*(n+1)*.1m+(i%3)*.1m;
    var value=a.DatasetType==HistoricalDatasetType.Institutional?new HistoricalValue(Foreign:10+n,Trust:5,Dealer:1):new(price,price+1,price-1,price,1000+i,100000+i);
    var f=new HistoricalFact("fixture",a.DatasetType,id.ToString(),date,At(date).AddHours(-18),At(date),created,created,"fixture-revision",$"{a.DatasetType}/{id}/{date:yyyy-MM-dd}",a.ArchiveId,value,"Shares","TWD",EvidenceStatus.Available,a.SourceChecksum,"",policies.Single(x=>x.DatasetType==a.DatasetType).PolicyId,AvailabilityConfidence.Known,"fixture publication","fixture revision","fixture-raw-v1");
    facts.Add(f with{FactChecksum=HistoricalAvailability.FactChecksum(f)});
}
var days=new[]{dates[130],dates[135],dates[140]};
var universes=days.Select((d,i)=>
{
    var u=new HistoricalUniverse(Id(30+i),"fixture-universe-v1",d,before,ids.ToImmutableArray(),"Simulated membership","fixture://universe",true,"");
    return u with{Checksum=HistoricalAvailability.UniverseChecksum(u)};
}).ToImmutableArray();
var dataset=new HistoricalBuildRequest("authority-test-v1","pit-builder-v1",created,Id(20),Id(21),V3CandidateCalculator.ParameterChecksum(p),new("fixture-calendar-v1",dates,before,"fixture://calendar"),
    new("fixture-raw-v1","Raw","fixture-corporate-actions-v1","fixture://actions","fixture reviewer",true),days.Select(d=>new HistoricalResearchDate(d,At(d).AddHours(1))).ToImmutableArray(),archives,facts.ToImmutableArray(),policies,
    ids.Append(benchmark).Select(id=>new HistoricalInstrumentMapping("fixture",id.ToString(),id,dates[0],null,before,"fixture://identity","fixture-id-v1")).ToImmutableArray(),universes,
    ids.Select(id=>new HistoricalBenchmarkMapping(id,benchmark,Id(22),dates[0],null,before,"fixture-benchmark-v1","fixture://benchmark")).ToImmutableArray());
var evidence=archives.Select((a,i)=>
{
    var bytes=Encoding.UTF8.GetBytes("SIMULATED PUBLICATION AND REVISION SPECIFICATION "+a.DatasetType);
    return new HistoricalAvailabilityEvidence(Id(40+i),"fixture",a.DatasetType,a.SourceUri,AvailabilityEvidenceType.OfficialSpecification,"fixture://spec",before,
        Convert.ToHexString(SHA256.HashData(bytes)),"fixture-publication-v1",dates[0],null,"UTC",EvidenceVerificationStatus.Verified,"Test reviewer attestation only",Convert.ToBase64String(bytes));
}).ToImmutableArray();
var authorities=archives.Select((a,i)=>new HistoricalDataAuthority(Id(50+i),"fixture",a.DatasetType,a.SourceUri,"fixture-publication-v1","fixture-revision-v1","fixture-calendar-v1","fixture-raw-v1",dates[0],null,[Id(40+i)],created,"fixture reviewer",AuthorityStatus.Approved,"TEST_ONLY",HistoricalAuthority.PolicyScopeDigest(dataset,policies[i]))).ToImmutableArray();
var bindings=dataset.Facts.Select(f=>new AuthorityFactBinding(f.FactChecksum,authorities.Single(a=>a.DatasetType==f.DatasetType).AuthorityId,"fixture-revision-v1")).ToImmutableArray();
var submission=new AuthoritySubmission(dataset,authorities,evidence,bindings,created,null);
var digest=HistoricalAuthority.SubmissionDigest(submission);
submission=submission with{Review=new(digest,AuthorityStatus.Approved,created,"fixture reviewer","TEST_ONLY complete dataset review")};

return(submission,p);
}
}