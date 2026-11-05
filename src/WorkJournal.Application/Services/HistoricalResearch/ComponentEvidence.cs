using System.Globalization;
using System.Collections.Immutable;
using System.Text.Json;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.HistoricalResearch.Components;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

public static class ComponentEvidence
{
    public const string ParserVersion="TWSE-STOCK_DAY-row-v1";
    public static bool ProviderRevision(string? revision,string checksum)=>!string.IsNullOrWhiteSpace(revision)&&revision!=checksum&&
        !revision.StartsWith("captured-body:",StringComparison.OrdinalIgnoreCase);
    public static ComponentAuthorityDependencyManifest Dependencies(EvidenceCode component,Guid parameters,DateTimeOffset at)
    {
        if(component is not EvidenceCode.Trend and not EvidenceCode.Momentum and not EvidenceCode.VolumePrice)throw new ArgumentException("First component scope is Trend/Momentum/VolumePrice.");
        var m=new ComponentAuthorityDependencyManifest(component,V3CandidateCalculator.Version,parameters,Enum.GetValues<ClosureDomain>(),
            component==EvidenceCode.VolumePrice?["HistoricalPrice","HistoricalVolume"]:["HistoricalPrice"],
            ["Calendar","Availability","Revision","UniverseEligibility","CorporateActionPriceBasis"],
            ["InstrumentId","HistoricalUniverseSnapshot","BenchmarkVersionIdentity"],[],["Institutional","BenchmarkReturns","FullEvidence100"],at,"component-dependencies-v1","");
        return m with{Digest=ResearchMath.Digest(m)};
    }
    public static Guid? ResolveInstrument(HistoricalInstrumentIdentityRecord[] identities,string market,string symbol,DateTimeOffset timestamp,DateTimeOffset cutoff)
    {
        var matches=identities.Where(i=>i.Market==market&&i.Symbol==symbol&&i.ValidFrom<=timestamp&&(!i.ValidTo.HasValue||timestamp<i.ValidTo)&&
            i.EffectiveAt<=timestamp&&i.AvailableAt.HasValue&&i.AvailableAt<=cutoff&&i.InstrumentId!=Guid.Empty&&i.EvidenceId!=Guid.Empty&&!string.IsNullOrWhiteSpace(i.RevisionId)).ToArray();
        return matches.Length==1?matches[0].InstrumentId:null;
    }
    public static CalendarCoverage Calendar(HistoricalTradingSession[] sessions,DateOnly start,DateOnly end,DateTimeOffset cutoff)
    {
        if(end<start)throw new ArgumentException("Invalid calendar range.");
        var known=new List<HistoricalTradingSession>();int unknown=0;
        for(var date=start;date<=end;date=date.AddDays(1))
        {
            var rows=sessions.Where(s=>s.TradeDate==date&&s.AvailableAt.HasValue&&s.AvailableAt<=cutoff&&s.SourceEvidenceId!=Guid.Empty&&
                s.SessionType!=SessionKind.Unknown&&s.IsTradingDay.HasValue&&!string.IsNullOrWhiteSpace(s.PolicyVersion)).ToArray();
            if(rows.Length!=1||rows[0].IsTradingDay!=(rows[0].SessionType is SessionKind.NormalTradingDay or SessionKind.SpecialSession)||
                (rows[0].IsTradingDay==true&&(rows[0].OpenTime is null||rows[0].CloseTime is null||rows[0].OpenTime>=rows[0].CloseTime)))unknown++;
            else known.Add(rows[0]);
        }
        var verified=known.Count(s=>s.IsTradingDay==true);int? expected=unknown==0?verified:null;
        return new(expected,verified,unknown,known.Count(s=>s.SessionType==SessionKind.ScheduledHoliday),known.Count(s=>s.SessionType==SessionKind.EmergencyClosure),expected>0?(decimal)verified/expected:null);
    }
    public static ComponentAvailability Availability(HistoricalFact f,HistoricalAvailabilityClaim? claim,DateTimeOffset cutoff)
    {
        if(claim is null||claim.FactId!=f.FactChecksum||claim.Confidence==AvailabilityConfidence.Unknown||claim.EvidenceType>AuthorityLevel.OfficialDocumentation||
            claim.EvidenceIds.Length==0||claim.Provider!=f.Provider||claim.DatasetType!=f.DatasetType||claim.TradeDate!=f.TradeDate)return ComponentAvailability.Unverifiable;
        var at=claim.RevisionAvailableAt??claim.OriginalAvailableAt;
        if(at is null)return ComponentAvailability.Unverifiable;
        if(at>cutoff)return ComponentAvailability.FutureKnown;
        if(f.QualityStatus==EvidenceStatus.Stale)return ComponentAvailability.Stale;
        if(f.QualityStatus==EvidenceStatus.Invalid)return ComponentAvailability.Invalid;
        if(f.Value.Close is null)return ComponentAvailability.Missing;
        return f.Value.Close<=0?ComponentAvailability.Invalid:ComponentAvailability.Available;
    }
    public static bool PriceBasis(string basis,string derived,string policy,bool frozen)=>
        new[]{"RAW","ADJUSTED","TOTAL_RETURN"}.Contains(basis.ToUpperInvariant())&&basis==derived&&frozen&&!string.IsNullOrWhiteSpace(policy);
    public static HistoricalCorporateAction[] VisibleActions(HistoricalCorporateAction[] actions,DateTimeOffset cutoff)=>
        actions.Where(a=>a.AvailableAt.HasValue&&a.AvailableAt<=cutoff).ToArray();
    public static ParsedPriceRow[] ParsePrice(string evidenceRoot,Guid evidenceId)
    {
        var artifact=new EvidenceAcquisitionStore(evidenceRoot).ReadArtifact(evidenceId);
        var uri=new Uri(artifact.SourceReference);
        if(artifact.Provider!="TWSE"||uri.Host!="www.twse.com.tw"||uri.AbsolutePath!="/rwd/zh/afterTrading/STOCK_DAY")throw new InvalidDataException("Unsupported source/parser identity.");
        var query=uri.Query.TrimStart('?').Split('&').Select(x=>x.Split('=',2)).ToDictionary(x=>x[0],x=>x.Length==2?Uri.UnescapeDataString(x[1]):"");
        if(!query.TryGetValue("stockNo",out var symbol)||string.IsNullOrWhiteSpace(symbol))throw new InvalidDataException("Missing symbol.");
        using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(evidenceRoot,"evidence","authority",evidenceId.ToString("N"),"raw.bin")));
        var root=doc.RootElement;if(root.GetProperty("stat").GetString()!="OK")throw new InvalidDataException("Not a stock-day response.");
        var fields=root.GetProperty("fields").EnumerateArray().Select(x=>x.GetString()).ToArray();
        string[] expected=["日期","成交股數","成交金額","開盤價","最高價","最低價","收盤價"];
        if(!fields.Take(expected.Length).SequenceEqual(expected))throw new InvalidDataException("Changed source schema.");
        decimal? Number(JsonElement x)=>decimal.TryParse(x.GetString()?.Replace(",",""),NumberStyles.Number,CultureInfo.InvariantCulture,out var n)?n:null;
        return root.GetProperty("data").EnumerateArray().Select((row,index)=>
        {
            var date=row[0].GetString()!.Split('/').Select(x=>int.Parse(x,CultureInfo.InvariantCulture)).ToArray();
            var d=new DateOnly(date[0]+1911,date[1],date[2]);
            var value=new HistoricalValue(Number(row[3]),Number(row[4]),Number(row[5]),Number(row[6]),Number(row[1]),Number(row[2]));
            return new ParsedPriceRow(new(evidenceId,artifact.SHA256,index,d,symbol),value,ResearchMath.Digest(value),ParserVersion);
        }).ToArray();
    }
    public static AuthorityClaimResult[] Claims(AuthorityClosureStore store,Guid id)
    {
        var previous=new Dictionary<string,AuthorityClaimResult>(StringComparer.Ordinal);
        foreach(var latest in store.History(id))
        {
            var c=latest.Case;var reviews=store.Reviews(latest);
            foreach(var x in latest.Claims)
            {
                var scoped=reviews.Where(r=>r.EffectiveFrom<=c.TargetStart&&r.EffectiveTo>=c.TargetEnd).ToArray();
                var status=x.Status switch{"SUPPORTED"=>ClaimSupport.SUPPORTED,"CONFLICTING"=>ClaimSupport.CONFLICTING,_=>ClaimSupport.UNKNOWN};
                if(status==ClaimSupport.UNKNOWN&&scoped.Any(r=>r.Unsupported.Contains(x.Claim)))status=ClaimSupport.UNSUPPORTED;
                if(status==ClaimSupport.UNKNOWN&&reviews.Any(r=>r.Supported.Contains(x.Claim)&&r.EffectiveFrom<=c.TargetEnd&&r.EffectiveTo>=c.TargetStart))status=ClaimSupport.PARTIALLY_SUPPORTED;
                var ids=reviews.Where(r=>r.Supported.Contains(x.Claim)||r.Unsupported.Contains(x.Claim)||r.Ambiguous.Contains(x.Claim)).Select(r=>r.EvidenceId).Distinct().Order().ToArray();
                var r=new AuthorityClaimResult(c.CaseId+":"+x.Claim,c.Domain,x.Claim,c.SubjectDigest,c.TargetStart,c.TargetEnd,ids,
                    ResearchMath.Digest(new{latest.ReviewDigests,EvidenceIds=ids}),status,status==ClaimSupport.SUPPORTED?"REVIEWED_SCOPE":"UNPROVEN",
                    status==ClaimSupport.CONFLICTING?"CONFLICTING":"NO_RECORDED_CONFLICT",latest.Reviewer,latest.At,latest.Reason,x.Reasons,
                    latest.Sequence,previous.TryGetValue(x.Claim,out var old)?old.RevisionDigest:"","");previous[x.Claim]=r with{RevisionDigest=ResearchMath.Digest(r)};
            }
        }
        return previous.Values.OrderBy(r=>r.ClaimType,StringComparer.Ordinal).ToArray();
    }
    public static AuthorityEvidenceRegistry Registry(EvidenceArtifact a,string[] claims,AuthorityLevel level)=>new(a.EvidenceId,a.Provider,a.DatasetType,a.SourceReference,a.CapturedAt,
        a.SHA256,a.ContentType,a.TargetStart,a.TargetEnd,claims,level,$"evidence/authority/{a.EvidenceId:N}/raw.bin",a.ParserVersion,"evidence-registry-v1");
    public static ComponentDatasetInput LinkPrice(ComponentDatasetInput input,string evidenceRoot,Guid evidenceId)
    {
        var a=new EvidenceAcquisitionStore(evidenceRoot).ReadArtifact(evidenceId);var rows=ParsePrice(evidenceRoot,evidenceId);
        if(rows.Length==0)throw new InvalidDataException("Empty price response.");
        var bytes=File.ReadAllBytes(Path.Combine(evidenceRoot,"evidence","authority",evidenceId.ToString("N"),"raw.bin"));
        var archive=new SourceArchive(evidenceId.ToString("N"),a.Provider,HistoricalDatasetType.Price,a.SourceReference,a.CapturedAt,a.CapturedAt,
            Convert.ToBase64String(bytes),a.SHA256,"TWSE-capture-v1",false);
        var capture=OfficialHistoricalSource.Parse(archive,rows[0].Locator.TradeDate,rows[0].Locator.Symbol);
        var old=input.Closure.Dataset;var d=old.Source.Dataset;var now=DateTimeOffset.UtcNow;
        d=d with{CreatedAt=now,Archives=d.Archives.Where(x=>x.DatasetType!=HistoricalDatasetType.Price).Append(archive).ToImmutableArray(),
            Facts=d.Facts.Where(f=>f.DatasetType!=HistoricalDatasetType.Price).Concat(capture.Facts).ToImmutableArray()};
        var snapshot=new HistoricalDatasetBuilder().Build(d);
        var lineage=capture.Facts.Select(f=>
        {
            var row=rows.Single(x=>x.Locator.TradeDate==f.TradeDate&&x.Value==f.Value);
            var mapping=input.Identities.Where(i=>i.Symbol==f.SourceInstrumentKey&&i.ValidFrom<=f.EffectiveAt&&(!i.ValidTo.HasValue||f.EffectiveAt<i.ValidTo)).ToArray();
            return new HistoricalFactLineage(f.FactChecksum,mapping.Length==1?mapping[0].InstrumentId:Guid.Empty,f.TradeDate,f.DatasetType,evidenceId,row.Locator,
                ParserVersion,row.ParsedValueDigest,null,null,snapshot.DatasetSnapshotId);
        }).ToArray();
        var request=input.Request with{RequestedAt=now,RequestDigest=""};request=request with{RequestDigest=ResearchMath.Digest(request)};
        return input with{Request=request,Closure=input.Closure with{Dataset=old with{Source=old.Source with{Dataset=d,EvaluatedAt=now,Review=null},Review=null}},Lineage=lineage};
    }
}
