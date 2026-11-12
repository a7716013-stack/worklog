using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;
public static class EtlQuality
{
    public static DataQualityReport Evaluate(ProviderRequest request,ProviderResponse response)
    {
        var bad=new List<QuarantinedRecord>();var checks=new List<QualityCheck>();
        var keys=response.Facts.GroupBy(f=>(f.SourceInstrumentKey,f.TradeDate,f.DatasetType)).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
        for(var i=0;i<response.Facts.Length;i++)
        {
            var f=response.Facts[i];var errors=new List<string>();
            if(f.SourceInstrumentKey!=request.Instrument)errors.Add("INSTRUMENT_IDENTITY_MISMATCH");
            if(f.Provider!=request.Provider||f.SourceChecksum!=request.SourceVersion)errors.Add("SOURCE_VERSION_MISMATCH");
            if(f.TradeDate<request.CoverageStart||f.TradeDate>request.CoverageEnd)errors.Add("DATE_OUTSIDE_SCOPE");
            if(f.EffectiveAt.Offset!=TimeSpan.FromHours(8)||DateOnly.FromDateTime(f.EffectiveAt.DateTime)!=f.TradeDate||f.RetrievedAt<f.ObservedAt)errors.Add("TEMPORAL_CONFLICT");
            if(keys.Contains((f.SourceInstrumentKey,f.TradeDate,f.DatasetType)))errors.Add("DUPLICATE_OR_REVISION_CONFLICT");
            if(f.DatasetType==HistoricalDatasetType.Price)
            {
                if(f.Value.Open is null||f.Value.High is null||f.Value.Low is null||f.Value.Close is null)errors.Add("MISSING_REQUIRED_PRICE");
                if(f.Value.Open<=0||f.Value.High<=0||f.Value.Low<=0||f.Value.Close<=0||f.Value.Low>f.Value.High||f.Value.Open<f.Value.Low||f.Value.Open>f.Value.High||f.Value.Close<f.Value.Low||f.Value.Close>f.Value.High||f.Value.Volume<0)errors.Add("INVALID_PRICE_OR_VOLUME");
            }
            else if(f.Value.Foreign is null||f.Value.Trust is null||f.Value.Dealer is null)errors.Add("MISSING_REQUIRED_INSTITUTIONAL");
            if(errors.Count>0)bad.Add(new(i,errors.ToArray(),f.FactChecksum));
        }
        foreach(var name in new[]{"Schema","RequiredFields","DateTimeZone","InstrumentIdentity","Units","Duplicates","Completeness","PriceBasis","RevisionConflict","SourceVersion","TemporalConsistency","RawTransformDigest"})
        {
            var warning=name is "PriceBasis" or "Completeness"||name=="Units"&&request.Provider=="TPEx";
            var fail=name switch{
                "RequiredFields"=>bad.Any(q=>q.Reasons.Any(e=>e.StartsWith("MISSING_")||e.StartsWith("INVALID_"))),
                "DateTimeZone" or "TemporalConsistency"=>bad.Any(q=>q.Reasons.Any(e=>e is "TEMPORAL_CONFLICT" or "DATE_OUTSIDE_SCOPE")),
                "InstrumentIdentity"=>bad.Any(q=>q.Reasons.Contains("INSTRUMENT_IDENTITY_MISMATCH")),
                "Duplicates" or "RevisionConflict"=>keys.Count>0,"SourceVersion"=>bad.Any(q=>q.Reasons.Contains("SOURCE_VERSION_MISMATCH")),_=>false};
            checks.Add(new(name,fail?"FAIL":warning?"UNKNOWN":"PASS",name switch{
                "PriceBasis"=>"Raw provider prices only; historical corporate-action approval remains separate",
                "Completeness"=>"Observed rows counted; expected historical universe/session denominator is unknown",
                "Units" when request.Provider=="TPEx"=>"Monthly rounded lots cannot establish exact shares; volume stays null",
                "InstrumentIdentity"=>"Requested symbol binding only; historical instrument authority remains separate",
                _=>fail?"See quarantined row reasons":"Validated for this transform scope"}));
        }
        if(response.Facts.Length==0)checks.Add(new("Records","FAIL","NO_DATA_FOR_REQUESTED_SCOPE_NOT_PROOF_OF_HISTORICAL_ABSENCE"));
        var r=new DataQualityReport(bad.Count>0||response.Facts.Length==0?"QUARANTINED":"PASSED_WITH_UNPROVEN_HISTORICAL_CONTEXT",response.Facts.Length,response.Facts.Length-bad.Count,bad.ToArray(),checks.ToArray(),"");
        return r with{Digest=ResearchMath.Digest(r)};
    }
}
