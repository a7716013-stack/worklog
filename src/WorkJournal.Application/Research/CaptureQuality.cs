using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;
public sealed record CaptureQualityContext(HistoricalCalendar? Calendar,HistoricalPricePolicy? PricePolicy,DateTimeOffset DecisionAt,string[] ReviewedContextDigests);
public sealed record CaptureQualityReport(DataQualityReport Transform,QualityCheck[] Context,string Status,string Authority,string Pit,string Dataset,string Digest);
public sealed record ReconciliationDifference(string Instrument,DateOnly Date,string Dataset,string? LeftFactDigest,string? RightFactDigest,string Status,string[] Fields);
public sealed record ReconciliationReport(string Policy,string LeftDigest,string RightDigest,ReconciliationDifference[] Differences,string Status,string Authority,string Digest);
public static class CaptureQuality
{
    public static CaptureQualityReport Evaluate(ProviderRequest request,ProviderResponse response,CaptureQualityContext context)
    {
        var quality=EtlQuality.Evaluate(request,response);var checks=new List<QualityCheck>();
        if(context.DecisionAt.Offset!=TimeSpan.Zero)throw new ArgumentException("UTC_DECISION_REQUIRED");
        var calendar=context.Calendar;var policy=context.PricePolicy;
        var calendarTrusted=calendar is not null&&calendar.AvailableAt is not null&&calendar.AvailableAt<=context.DecisionAt&&
            !string.IsNullOrWhiteSpace(calendar.Version)&&!string.IsNullOrWhiteSpace(calendar.SourceEvidence)&&
            calendar.Sessions.SequenceEqual(calendar.Sessions.Distinct().Order())&&context.ReviewedContextDigests.Contains(ResearchMath.Digest(calendar));
        checks.Add(new("TradingCalendar",calendarTrusted?(response.Facts.All(f=>calendar!.Sessions.Contains(f.TradeDate))?"PASS_CONTEXT_ONLY":"FAIL"):"UNKNOWN","Explicit reviewed calendar required; no inferred weekend/holiday membership"));
        var priceTrusted=policy is not null&&policy.Frozen&&new[]{policy.PriceBasisVersion,policy.Basis,policy.CorporateActionPolicyVersion,policy.SourceEvidence,policy.ReviewReference}.All(x=>!string.IsNullOrWhiteSpace(x))&&context.ReviewedContextDigests.Contains(ResearchMath.Digest(policy));
        checks.Add(new("CorporateActionPriceBasis",priceTrusted?(response.Facts.Where(f=>f.DatasetType!=HistoricalDatasetType.Institutional).All(f=>f.PriceBasisVersion==policy!.PriceBasisVersion)?"PASS_CONTEXT_ONLY":"FAIL"):"UNKNOWN","Existing frozen policy receipt required; do not infer split/dividend adjustments"));
        checks.Add(new("VolumeUnit",response.Facts.Where(f=>f.DatasetType==HistoricalDatasetType.Price).All(f=>f.Value.Volume is not null&&f.Unit=="SHARES")?"PASS_CONTEXT_ONLY":"UNKNOWN","Do not convert rounded lots to exact shares or treat missing volume as zero"));
        checks.Add(new("HistoricalPublication",response.Facts.All(f=>f.AvailableAt is not null&&f.AvailableAt<=context.DecisionAt)?"REVIEW_REQUIRED":"UNKNOWN","ObservedAt/RetrievedAt never substitute for original PublishedAt/AvailableAt"));
        checks.Add(new("DecisionTimeAlignment",response.Facts.Any(f=>f.ObservedAt>context.DecisionAt||f.RetrievedAt>context.DecisionAt||f.AvailableAt>context.DecisionAt)?"FAIL":"REVIEW_REQUIRED","Reject using a later capture at an earlier decision; passing is not PIT eligibility"));
        checks.Add(new("FactIntegrity",response.Facts.All(f=>f.FactChecksum==HistoricalAvailability.FactChecksum(f)&&f.SourceChecksum==response.RawDigest)?"PASS_CONTEXT_ONLY":"FAIL","Preserve original normalized fact and raw version bindings"));
        var result=new CaptureQualityReport(quality,checks.ToArray(),quality.Status=="QUARANTINED"||checks.Any(c=>c.Status=="FAIL")?"QUARANTINED":"REVIEW_REQUIRED","REVIEW_REQUIRED","NOT_EVALUATED","NOT_APPROVED","");
        return result with{Digest=ResearchMath.Digest(result)};
    }
    public static ReconciliationReport Reconcile(HistoricalFact[] left,HistoricalFact[] right)
    {
        if(left.Concat(right).Any(f=>f.FactChecksum!=HistoricalAvailability.FactChecksum(f)))throw new InvalidDataException("RECONCILIATION_FACT_DIGEST_INVALID");
        if(left.Select(f=>f.Provider).Distinct().Count()>1||right.Select(f=>f.Provider).Distinct().Count()>1||left.Length>0&&right.Length>0&&left[0].Provider==right[0].Provider)throw new ArgumentException("DISTINCT_PROVIDERS_REQUIRED");
        var output=new List<ReconciliationDifference>();
        var keys=left.Concat(right).Select(f=>(Instrument:f.SourceInstrumentKey,Date:f.TradeDate,Dataset:f.DatasetType)).Distinct().OrderBy(k=>k.Instrument,StringComparer.Ordinal).ThenBy(k=>k.Date).ThenBy(k=>k.Dataset);
        foreach(var key in keys)
        {
            var a=left.Where(f=>(f.SourceInstrumentKey,f.TradeDate,f.DatasetType)==key).ToArray();var b=right.Where(f=>(f.SourceInstrumentKey,f.TradeDate,f.DatasetType)==key).ToArray();
            string status;string[] fields=[];
            if(a.Length>1||b.Length>1)status="DUPLICATE_CONFLICT";
            else if(a.Length==0||b.Length==0)status="MISSING_COUNTERPART";
            else if(a[0].Unit!=b[0].Unit||a[0].Unit.Contains("UNKNOWN")||a[0].Currency!=b[0].Currency||a[0].PriceBasisVersion!=b[0].PriceBasisVersion||string.IsNullOrWhiteSpace(a[0].PriceBasisVersion))status="INCOMPARABLE_CONTEXT";
            else {
                var x=a[0].Value;var y=b[0].Value;
                (string Name,decimal? Left,decimal? Right)[] values=[("Open",x.Open,y.Open),("High",x.High,y.High),("Low",x.Low,y.Low),("Close",x.Close,y.Close),("Volume",x.Volume,y.Volume),("TradingValue",x.TradingValue,y.TradingValue),("Foreign",x.Foreign,y.Foreign),("Trust",x.Trust,y.Trust),("Dealer",x.Dealer,y.Dealer)];
                fields=values.Where(v=>v.Left!=v.Right).Select(v=>v.Name).ToArray();status=fields.Length>0?"DIFFERENT":"MATCH_REVIEW_REQUIRED";
            }
            output.Add(new(key.Instrument,key.Date,key.Dataset.ToString(),a.Length==1?a[0].FactChecksum:null,b.Length==1?b[0].FactChecksum:null,status,fields));
        }
        var result=new ReconciliationReport("ExactDecimal-v1",ResearchMath.Digest(left),ResearchMath.Digest(right),output.ToArray(),output.Count==0?"NOT_RUN":output.Any(d=>d.Status!="MATCH_REVIEW_REQUIRED")?"QUARANTINED":"REVIEW_REQUIRED","NOT_APPROVED","");
        return result with{Digest=ResearchMath.Digest(result)};
    }
}
