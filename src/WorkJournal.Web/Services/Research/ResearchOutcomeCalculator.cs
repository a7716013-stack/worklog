using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Models.V2;
namespace WorkJournal.Web.Services.Research;

public static class ResearchOutcomeCalculator
{
    public static ResearchOutcome Calculate(CandidateTrace trace,int horizon,OutcomeDataset data,decimal? evidenceClose)
    {
        if(horizon<=0)throw new ArgumentOutOfRangeException(nameof(horizon));
        ResearchOutcome Missing(DateOnly? end,string reason)=>new(horizon,end,null,null,null,reason);
        var start=data.Sessions.IndexOf(trace.Identity.TradeDate);
        if(start<0 || horizon>=data.Sessions.Length-start)return Missing(null,"INSUFFICIENT_FUTURE_SESSIONS");
        var end=data.Sessions[start+horizon];
        var series=data.Series.Where(s=>s.InstrumentId==trace.InstrumentId).ToArray();
        if(series.Length!=1)return Missing(end,"OUTCOME_SERIES_MISSING_OR_DUPLICATE");
        if(series[0].PriceBasisVersion!=trace.Identity.PriceBasisVersion)return Missing(end,"PRICE_BASIS_MISMATCH");
        var closes=new List<decimal>();
        foreach(var date in data.Sessions.Skip(start).Take(horizon+1))
        {
            // Fixed versioned outcome snapshot, not a latest-provider lookup.
            var matches=series[0].Closes.Where(c=>c.Date==date && (c.AvailableAt is null||c.AvailableAt<=data.EvaluationCutoff)).ToArray();
            if(matches.Length!=1)return Missing(end,"OUTCOME_MISSING_OR_DUPLICATE");
            var c=matches[0];
            if(c.AvailableAt is null)return Missing(end,"OUTCOME_AVAILABILITY_UNVERIFIABLE");
            if(c.Status!=EvidenceStatus.Available||c.Close<=0)return Missing(end,"OUTCOME_INVALID_OR_UNAVAILABLE");
            closes.Add(c.Close);
        }
        if(evidenceClose is null || evidenceClose<=0 || closes[0]!=evidenceClose)return Missing(end,"EVIDENCE_OUTCOME_BASE_CLOSE_MISMATCH");
        var baseline=closes[0];var future=closes.Skip(1).Select(c=>c/baseline-1).ToArray();
        return new(horizon,end,future[^1],future.Max(),future.Min(),"AVAILABLE_RESEARCH_LABEL_NOT_EXECUTABLE_PNL");
    }
}
