using System.Collections.Immutable;
using WorkJournal.Web.Models.Research;
namespace WorkJournal.Web.Services.Research;

public static class ResearchMetrics
{
    public static ResearchMetric Calculate(string fold,ResearchPartition partition,string variable,int horizon,
        IEnumerable<ResearchValue> source,int quantiles,int purged)
    {
        var rows=source.OrderBy(x=>x.Date).ThenBy(x=>x.InstrumentId).ToArray();
        var assigned=new List<ResearchValue>();
        foreach(var group in rows.GroupBy(x=>x.Date))
        {
            var valid=group.Where(x=>x.Value.HasValue).ToArray();
            var bins=ResearchMath.Quantiles(valid.Select(x=>x.Value!.Value).ToArray(),quantiles);
            assigned.AddRange(valid.Select((r,i)=>r with {Quantile=bins[i]}));
        }
        // Quantiles depend on evidence only; outcome missingness cannot change the ranking universe.
        var pairs=assigned.Where(x=>x.Outcome.HasValue).ToArray();
        var daily=rows.Select(x=>x.Date).Distinct().Select(date=>{
            var a=pairs.Where(x=>x.Date==date).ToArray();var x=a.Select(r=>r.Value!.Value).ToArray();var y=a.Select(r=>r.Outcome!.Value).ToArray();
            return new DailyIc(date,a.Length,ResearchMath.Correlation(x,y),ResearchMath.RankIc(x,y));
        }).ToImmutableArray();
        var qs=Enumerable.Range(1,quantiles).Select(q=>{
            var outcomes=pairs.Where(x=>x.Quantile==q).Select(x=>x.Outcome!.Value).ToArray();
            return new QuantileResult(q,outcomes.Length,ResearchMath.Mean(outcomes),ResearchMath.Median(outcomes),outcomes.Length==0?null:(decimal)outcomes.Count(x=>x>0)/outcomes.Length);
        }).ToImmutableArray();
        var adjacent=qs.Zip(qs.Skip(1)).Where(p=>p.First.MeanReturn.HasValue&&p.Second.MeanReturn.HasValue).ToArray();
        var turnover=new List<decimal>();HashSet<Guid>? previous=null;
        foreach(var date in rows.Select(x=>x.Date).Distinct())
        {
            var top=assigned.Where(x=>x.Date==date&&x.Quantile==quantiles).Select(x=>x.InstrumentId).ToHashSet();
            if(previous is not null && previous.Count>0 && top.Count>0)
                turnover.Add(1m-(decimal)top.Intersect(previous).Count()/top.Union(previous).Count());
            previous=top; // Explicit Jaccard top-quantile turnover proxy, not portfolio turnover.
        }
        var ranks=daily.Where(x=>x.RankIc.HasValue).Select(x=>x.RankIc!.Value).ToArray();
        var available=rows.Count(x=>x.Value.HasValue);var coverage=rows.Length==0?0:(decimal)available/rows.Length;
        return new(fold,partition,variable,horizon,rows.Length,available,pairs.Length,coverage,rows.Length==0?0:1-coverage,purged,
            ResearchMath.Mean(pairs.Select(x=>x.Outcome!.Value)),ResearchMath.Median(pairs.Select(x=>x.Outcome!.Value)),daily,
            ResearchMath.Mean(daily.Where(x=>x.PearsonIc.HasValue).Select(x=>x.PearsonIc!.Value)),ResearchMath.Mean(ranks),ResearchMath.Median(ranks),ResearchMath.StdDev(ranks),
            ranks.Length==0?null:(decimal)ranks.Count(x=>x>0)/ranks.Length,qs,
            qs[^1].MeanReturn-qs[0].MeanReturn,adjacent.Length==0?null:(decimal)adjacent.Count(x=>x.Second.MeanReturn>x.First.MeanReturn)/adjacent.Length,
            ResearchMath.Mean(turnover),ResearchMath.Distribution(rows.Select(x=>x.Value)));
    }
}
