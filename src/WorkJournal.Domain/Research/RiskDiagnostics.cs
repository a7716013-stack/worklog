using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Domain.Research;
// Future labels are research-only sidecars and never inputs to the evidence calculator.
public sealed record FutureRiskPoint(DateOnly Date,decimal Close,decimal? TradingValue);
public sealed record FutureRiskLabels(decimal? Volatility,decimal? Drawdown,decimal? Downside,decimal? LiquidityStability);
public static class RiskDiagnostics
{
    public static FutureRiskLabels Labels(decimal anchor,DateOnly observation,IReadOnlyList<DateOnly> expectedSessions,IEnumerable<FutureRiskPoint> source)
    {
        if(anchor<=0||expectedSessions.Count==0||expectedSessions.Any(d=>d<=observation)||expectedSessions.Distinct().Count()!=expectedSessions.Count||!expectedSessions.SequenceEqual(expectedSessions.Order()))throw new ArgumentException("Explicit future trading sessions required");
        var rows=source.OrderBy(x=>x.Date).ToArray();
        if(!rows.Select(x=>x.Date).SequenceEqual(expectedSessions)||rows.Any(x=>x.Close<=0))return new(null,null,null,null);
        var previous=anchor;var peak=anchor;var drawdown=0m;var returns=new List<decimal>();
        foreach(var row in rows) {returns.Add(row.Close/previous-1);previous=row.Close;peak=Math.Max(peak,row.Close);drawdown=Math.Max(drawdown,1-row.Close/peak);}
        var downside=WorkJournal.Web.Services.Evidence.CandidateMath.Sqrt(returns.Average(x=>Math.Min(x,0)*Math.Min(x,0)));
        decimal? stability=null;
        if(rows.All(x=>x.TradingValue is >=0)&&rows.Average(x=>x.TradingValue!.Value)>0)
            stability=ResearchMath.StdDev(rows.Select(x=>x.TradingValue!.Value))/rows.Average(x=>x.TradingValue!.Value);
        return new(ResearchMath.StdDev(returns),drawdown,downside,stability);
    }
    // Partial IC controls another evidence by residualizing both the feature and the outcome.
    public static decimal? PartialIc(IReadOnlyList<decimal> x,IReadOnlyList<decimal> y,IReadOnlyList<decimal> control)
    {
        if(x.Count!=y.Count||x.Count!=control.Count)throw new ArgumentException("Paired samples required");
        if(x.Count<3)return null;
        var mean=control.Average();var variance=control.Sum(c=>(c-mean)*(c-mean));if(variance==0)return null;
        decimal[] Residual(IReadOnlyList<decimal> values) {var m=values.Average();var slope=values.Select((v,i)=>(v-m)*(control[i]-mean)).Sum()/variance;return values.Select((v,i)=>v-m-slope*(control[i]-mean)).ToArray();}
        return ResearchMath.Correlation(Residual(x),Residual(y));
    }
}
