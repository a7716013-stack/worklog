using System.Collections.Immutable;
using System.Security.Cryptography;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

public interface IHistoricalResearchDatasetBuilder
{
    HistoricalBuildResult Build(HistoricalBuildRequest request);
}

// Pure preparation boundary: no network, clock, scoring, labels or parameter selection.
public sealed class HistoricalDatasetBuilder : IHistoricalResearchDatasetBuilder
{
    public static HistoricalBuildRequest Canonicalize(HistoricalBuildRequest r)=>r with
    {
        Dates=r.Dates.OrderBy(x=>x.TradeDate).ThenBy(x=>x.DataCutoff).ToImmutableArray(),
        Archives=r.Archives.OrderBy(x=>x.ArchiveId,StringComparer.Ordinal).ToImmutableArray(),
        Facts=r.Facts.OrderBy(x=>x.FactChecksum,StringComparer.Ordinal).ToImmutableArray(),
        Policies=r.Policies.OrderBy(x=>x.PolicyId).ToImmutableArray(),
        Instruments=r.Instruments.OrderBy(ResearchMath.Digest,StringComparer.Ordinal).ToImmutableArray(),
        Universes=r.Universes.Select(x=>x with{Members=x.Members.Order().ToImmutableArray()}).OrderBy(x=>x.AsOfDate).ThenBy(x=>x.SnapshotId).ToImmutableArray(),
        Benchmarks=r.Benchmarks.OrderBy(ResearchMath.Digest,StringComparer.Ordinal).ToImmutableArray()
    };
    public HistoricalBuildResult Build(HistoricalBuildRequest request)
    {
        var r=Canonicalize(request);
        if(r.VersionSetId==Guid.Empty||r.ParameterSetId==Guid.Empty||r.Dates.Length==0||
           new[]{r.DatasetVersion,r.BuildVersion,r.ParameterChecksum}.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Explicit dataset/build/parameter identities and research dates required.");
        if(r.Dates.Select(x=>x.TradeDate).Distinct().Count()!=r.Dates.Length||
           r.Dates.Any(x=>x.DataCutoff>r.CreatedAt)||r.Policies.Select(x=>x.PolicyId).Distinct().Count()!=r.Policies.Length||
           r.Archives.Select(x=>x.ArchiveId).Distinct().Count()!=r.Archives.Length)
            throw new ArgumentException("Duplicate identities or build before cutoff.");
        var input=ResearchMath.Digest(r);
        var gaps=new List<ResearchGap>();var decisions=new List<HistoricalFactDecision>();var snapshots=new List<ResearchSnapshot>();
        void Gap(string code,string detail)=>gaps.Add(new(code,detail));
        var archives=r.Archives.ToDictionary(x=>x.ArchiveId);
        var invalidArchives=new HashSet<string>();
        foreach(var a in r.Archives)
        {
            try
            {
                if(Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(a.BodyBase64)))!=a.SourceChecksum||
                   a.ObservedAt>a.RetrievedAt||a.RetrievedAt>r.CreatedAt||
                   new[]{a.ArchiveId,a.Provider,a.SourceUri,a.ParserVersion}.Any(string.IsNullOrWhiteSpace))invalidArchives.Add(a.ArchiveId);
            }
            catch(FormatException){invalidArchives.Add(a.ArchiveId);}
        }
        var duplicates=r.Facts.GroupBy(f=>(f.Provider,f.DatasetType,f.SourceRecordId,f.RevisionId)).Where(g=>g.Count()>1).SelectMany(g=>g).ToHashSet();
        var duplicateCount=r.Facts.Count(duplicates.Contains);
        var invalid=new HashSet<string>();var stale=new HashSet<string>();
        var selectedByDate=new Dictionary<DateOnly,List<(HistoricalFact Fact,Guid Id,DateTimeOffset At)>>();
        foreach(var day in r.Dates)
        {
            var selected=new List<(HistoricalFact Fact,Guid Id,DateTimeOffset At)>();
            foreach(var f in r.Facts)
            {
                string? reason=null;Guid? id=null;
                var policy=r.Policies.SingleOrDefault(p=>p.PolicyId==f.AvailabilityPolicyId);
                var (at,availability)=HistoricalAvailability.Resolve(f,policy);
                if(duplicates.Contains(f))reason="DATA_GAP:DUPLICATE_SOURCE_REVISION";
                else if(f.FactChecksum!=HistoricalAvailability.FactChecksum(f)||!archives.TryGetValue(f.ArchiveId,out var a)||
                    invalidArchives.Contains(f.ArchiveId)||f.SourceChecksum!=a.SourceChecksum||f.Provider!=a.Provider||f.DatasetType!=a.DatasetType||
                    f.ObservedAt!=a.ObservedAt||f.RetrievedAt!=a.RetrievedAt||
                    new[]{f.RevisionId,f.SourceRecordId,f.SourceInstrumentKey}.Any(string.IsNullOrWhiteSpace))reason="DATA_GAP:INVALID_PROVENANCE";
                else if(f.QualityStatus!=EvidenceStatus.Available)reason="DATA_GAP:QUALITY_"+f.QualityStatus;
                else if(!ValuesValid(f))reason="DATA_GAP:INVALID_VALUE_OR_UNIT";
                else if(f.DatasetType!=HistoricalDatasetType.Institutional&&f.PriceBasisVersion!=r.PricePolicy.PriceBasisVersion)reason="CORPORATE_ACTION_GAP:PRICE_BASIS_MISMATCH";
                else if(at is null)reason=availability;
                else if(at>day.DataCutoff||f.TradeDate>day.TradeDate)reason="AVAILABILITY_GAP:FUTURE_RECORD_OR_REVISION";
                else
                {
                    var identities=r.Instruments.Where(m=>m.Provider==f.Provider&&m.SourceInstrumentKey==f.SourceInstrumentKey&&
                        m.ValidFrom<=f.TradeDate&&(!m.ValidTo.HasValue||m.ValidTo>=f.TradeDate)&&m.AvailableAt<=day.DataCutoff&&
                        m.InstrumentId!=Guid.Empty&&!string.IsNullOrWhiteSpace(m.SourceEvidence)&&!string.IsNullOrWhiteSpace(m.Version)).ToArray();
                    if(identities.Length!=1)reason="DATA_GAP:INSTRUMENT_IDENTITY";
                    else if(!r.Calendar.Sessions.Contains(f.TradeDate))reason="DATA_GAP:OUTSIDE_EXPLICIT_CALENDAR";
                    else{id=identities[0].InstrumentId;selected.Add((f,id.Value,at.Value));}
                }
                if(reason?.StartsWith("DATA_GAP",StringComparison.Ordinal)==true)invalid.Add(f.FactChecksum);
                if(f.QualityStatus==EvidenceStatus.Stale)stale.Add(f.FactChecksum);
                decisions.Add(new(f.FactChecksum,day.TradeDate,day.DataCutoff,id,at,reason is null,reason??"ELIGIBLE"));
            }
            // Latest eligible revision only. Equal publication times with conflicting source revisions fail closed.
            var resolved=new List<(HistoricalFact Fact,Guid Id,DateTimeOffset At)>();
            foreach(var group in selected.GroupBy(x=>(x.Id,x.Fact.TradeDate,x.Fact.DatasetType)))
            {
                var max=group.Max(x=>x.At);var latest=group.Where(x=>x.At==max).ToArray();
                if(latest.Length==1)resolved.Add(latest[0]);
                else Gap("DATA_GAP",FormattableString.Invariant($"Ambiguous revision for {group.Key.Id}/{group.Key.TradeDate:yyyy-MM-dd}/{group.Key.DatasetType} at {day.TradeDate:yyyy-MM-dd}."));
                foreach(var x in group.Where(x=>latest.Length!=1||x.At!=max))
                {
                    var index=decisions.FindIndex(d=>d.ResearchDate==day.TradeDate&&d.FactChecksum==x.Fact.FactChecksum&&d.Eligible);
                    if(index>=0)decisions[index]=decisions[index] with{Eligible=false,Reason=latest.Length!=1?"DATA_GAP:AMBIGUOUS_REVISION":"SUPERSEDED_ELIGIBLE_REVISION"};
                }
            }
            selectedByDate[day.TradeDate]=resolved;
            var universes=r.Universes.Where(u=>u.AsOfDate==day.TradeDate).ToArray();
            if(universes.Length!=1||!UniverseValid(universes[0],day.DataCutoff))
            {Gap("UNIVERSE_GAP",FormattableString.Invariant($"No verified historical membership for {day.TradeDate:yyyy-MM-dd}; current membership forbidden."));continue;}
            var u=universes[0];
            if(!u.IncludesDelistedHistory)Gap("SURVIVORSHIP_BIAS_RISK",$"Delisted-history coverage incomplete: {u.DefinitionVersion}.");
            if(!r.PricePolicy.Frozen||new[]{r.PricePolicy.PriceBasisVersion,r.PricePolicy.Basis,r.PricePolicy.CorporateActionPolicyVersion,r.PricePolicy.SourceEvidence,r.PricePolicy.ReviewReference}.Any(string.IsNullOrWhiteSpace))
            {Gap("CORPORATE_ACTION_GAP","Frozen reviewed price and corporate-action policy required.");continue;}
            if(r.Calendar.AvailableAt is null||r.Calendar.AvailableAt>day.DataCutoff||
               new[]{r.Calendar.Version,r.Calendar.SourceEvidence}.Any(string.IsNullOrWhiteSpace)||
               !r.Calendar.Sessions.Contains(day.TradeDate)||!r.Calendar.Sessions.SequenceEqual(r.Calendar.Sessions.Distinct().Order()))
            {Gap("DATA_GAP","Verified ordered historical exchange calendar required.");continue;}
            var maps=u.Members.Select(id=>r.Benchmarks.Where(b=>b.InstrumentId==id&&b.ValidFrom<=day.TradeDate&&
                (!b.ValidTo.HasValue||b.ValidTo>=day.TradeDate)).ToArray()).ToArray();
            if(maps.Any(m=>m.Length!=1)||maps.Select(m=>(m[0].BenchmarkInstrumentId,m[0].BenchmarkVersionId)).Distinct().Count()!=1||
               maps.Any(m=>m[0].BenchmarkVersionId==Guid.Empty||m[0].BenchmarkInstrumentId==Guid.Empty))
            {Gap("BENCHMARK_GAP",FormattableString.Invariant($"Explicit single historical benchmark definition per cross-section required: {day.TradeDate:yyyy-MM-dd}."));continue;}
            var bm=maps[0][0];
            var benchmarkVerified=maps.All(m=>m[0].AvailableAt<=day.DataCutoff&&!string.IsNullOrWhiteSpace(m[0].SourceEvidence)&&!string.IsNullOrWhiteSpace(m[0].Version));
            if(!benchmarkVerified)Gap("BENCHMARK_GAP",FormattableString.Invariant($"Mapping not PIT-available: {day.TradeDate:yyyy-MM-dd}; RelativeStrength blocked."));
            ImmutableArray<CandidateBar> Bars(Guid id,HistoricalDatasetType kind)=>resolved.Where(x=>x.Id==id&&x.Fact.DatasetType==kind)
                .OrderBy(x=>x.Fact.TradeDate).Select(x=>new CandidateBar(x.Fact.TradeDate,x.Fact.Value.Close!.Value,x.Fact.Value.High!.Value,
                    x.Fact.Value.Low!.Value,x.Fact.Value.Volume!.Value,x.At)).ToImmutableArray();
            var series=u.Members.Order().Select(id=>new CandidateSeries(id,Bars(id,HistoricalDatasetType.Price),resolved
                .Where(x=>x.Id==id&&x.Fact.DatasetType==HistoricalDatasetType.Institutional).OrderBy(x=>x.Fact.TradeDate)
                .Select(x=>new CandidateFlow(x.Fact.TradeDate,x.Fact.Value.Foreign!.Value,x.Fact.Value.Trust!.Value,x.Fact.Value.Dealer!.Value,x.At)).ToImmutableArray())).ToImmutableArray();
            var benchmark=benchmarkVerified?Bars(bm.BenchmarkInstrumentId,HistoricalDatasetType.Benchmark):[];
            var checksum=ResearchMath.Digest(new{Input=input,Day=day,Series=series,Benchmark=benchmark,Universe=u});
            var identity=new CandidateIdentity(Id(checksum),u.SnapshotId,bm.BenchmarkVersionId,r.VersionSetId,r.ParameterSetId,r.ParameterChecksum,
                checksum,u.Checksum,ResearchMath.Digest(new{Mappings=maps.SelectMany(x=>x),Bars=benchmark}),r.PricePolicy.PriceBasisVersion,day.TradeDate,day.DataCutoff);
            var data=new CandidateDataset(identity,FoundationStatus.Finalized,u.AvailableAt,
                benchmarkVerified?maps.Max(m=>m[0].AvailableAt):null,r.Calendar.Sessions.Where(s=>s<=day.TradeDate).ToImmutableArray(),u.Members.Order().ToImmutableArray(),series,benchmark);
            snapshots.Add(new(data,new(input,u.SourceEvidence,string.Join(";",maps.SelectMany(x=>x).Select(x=>x.SourceEvidence)),
                ResearchMath.Digest(r.Policies),ResearchMath.Digest(resolved.Select(x=>new{x.Fact.RevisionId,x.Fact.RevisionEvidence})),r.PricePolicy.SourceEvidence,
                r.Archives.Length==0||r.Archives.Any(a=>a.Synthetic))));
        }
        foreach(var d in decisions.Where(d=>!d.Eligible&&d.Reason!="SUPERSEDED_ELIGIBLE_REVISION").GroupBy(d=>d.Reason))
            Gap(d.Key.Split(':')[0],$"{d.Key}: {d.Count()} fact/date decisions.");
        // Denominators come from supplied historical membership + calendar, never today's catalog or observed rows alone.
        var expected=r.Dates.Sum(d=>r.Universes.Where(u=>u.AsOfDate==d.TradeDate&&UniverseValid(u,d.DataCutoff)).Sum(u=>u.Members.Length*r.Calendar.Sessions.Count(s=>s<=d.TradeDate)));
        var coverage=new List<DatasetCoverage>();
        void Coverage(string name,int n,int available)=>coverage.Add(new(name,n,available,n==0?null:(decimal)available/n,n==0?null:1m-(decimal)available/n));
        Coverage("Price",expected,snapshots.Sum(s=>s.Data.Series.Sum(x=>x.Bars.Length)));
        Coverage("Volume",expected,snapshots.Sum(s=>s.Data.Series.Sum(x=>x.Bars.Length)));
        Coverage("Institutional",expected,snapshots.Sum(s=>s.Data.Series.Sum(x=>x.Flows.Length)));
        Coverage("Benchmark",r.Dates.Sum(d=>r.Calendar.Sessions.Count(s=>s<=d.TradeDate)),snapshots.Sum(s=>s.Data.Benchmark.Length));
        Coverage("Universe",r.Dates.Length,r.Dates.Count(d=>r.Universes.Count(u=>u.AsOfDate==d.TradeDate&&UniverseValid(u,d.DataCutoff))==1));
        Coverage("Date",r.Dates.Length,snapshots.Count);
        Coverage("Instrument",snapshots.Sum(s=>s.Data.UniverseMembers.Length),snapshots.Sum(s=>s.Data.Series.Count(x=>x.Bars.Any(b=>b.Date==s.Data.Identity.TradeDate))));
        if(coverage.Any(c=>c.Coverage is null||c.Coverage<1))Gap("COVERAGE_GAP","See per-domain denominators; missing institutional data is not zero and never renormalizes scores.");
        if(!r.PricePolicy.Frozen)Gap("CORPORATE_ACTION_GAP","No frozen reviewed price policy.");
        if(r.Benchmarks.Length==0)Gap("BENCHMARK_GAP","No historical benchmark mapping supplied.");
        if(r.Facts.Length==0)Gap("DATA_GAP","No source facts supplied.");
        var quality=new DatasetQualityReport(r.Facts.Length,duplicateCount,invalid.Count,stale.Count,Rate(duplicateCount,r.Facts.Length),Rate(invalid.Count,r.Facts.Length),Rate(stale.Count,r.Facts.Length),coverage.ToImmutableArray(),r.Dates.Length,snapshots.Count,snapshots.SelectMany(s=>s.Data.UniverseMembers).Distinct().Count());
        var orderedGaps=gaps.Distinct().OrderBy(x=>x.Code,StringComparer.Ordinal).ThenBy(x=>x.Detail,StringComparer.Ordinal).ToImmutableArray();
        var ds=ResearchMath.Digest(new{input,Snapshots=snapshots,Decisions=decisions,Quality=quality,Gaps=orderedGaps});
        return new(Id(ds).ToString("N"),ds,r.DatasetVersion,r.BuildVersion,r.CreatedAt,r.CreatedAt,
            snapshots.Count==0?"BLOCKED_BY_DATA":orderedGaps.Length>0?"PARTIAL_RESEARCH_DATASET":"PIT_DATASET_READY_NOT_VALIDATED",
            snapshots.ToImmutableArray(),decisions.ToImmutableArray(),quality,orderedGaps,input);
    }
    private static decimal? Rate(int n,int total)=>total==0?null:(decimal)n/total;
    internal static Guid Id(string digest)=>new(Convert.FromHexString(digest[..32]));
    private static bool UniverseValid(HistoricalUniverse u,DateTimeOffset cutoff)=>u.SnapshotId!=Guid.Empty&&u.AvailableAt<=cutoff&&
        u.Members.Length>0&&!u.Members.Contains(Guid.Empty)&&u.Members.Distinct().Count()==u.Members.Length&&
        !new[]{u.DefinitionVersion,u.SourceEvidence,u.MembershipReason}.Any(string.IsNullOrWhiteSpace)&&u.Checksum==HistoricalAvailability.UniverseChecksum(u);
    private static bool ValuesValid(HistoricalFact f)
    {
        var v=f.Value;
        if(f.DatasetType==HistoricalDatasetType.Institutional)return f.Unit=="Shares"&&f.Currency=="TWD"&&v.Foreign.HasValue&&v.Trust.HasValue&&v.Dealer.HasValue;
        return Enum.IsDefined(f.DatasetType)&&f.Unit=="Shares"&&f.Currency=="TWD"&&v.Open>0&&v.Close>0&&v.Low>0&&v.High>=v.Low&&
            v.Open>=v.Low&&v.Open<=v.High&&v.Close>=v.Low&&v.Close<=v.High&&v.Volume>=0&&v.TradingValue>=0;
    }
}
