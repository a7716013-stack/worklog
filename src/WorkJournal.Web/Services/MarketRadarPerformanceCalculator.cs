using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public static class MarketRadarPerformanceCalculator
{
    public static readonly int[] Horizons=[1,3,5,10,20];
    public static RadarEvaluation Evaluate(IEnumerable<SwingBar> input,DateOnly after,DateOnly through,decimal? fixedEntry=null)
    {
        var all=SwingCalculator.Normalize(input.Where(x=>x.Date>after&&x.Date<=through));
        var rows=all.Where(x=>x.Close>0 && x.Open>0 && x.High>0 && x.Low>0 && x.High>=x.Low && x.High>=x.Close && x.Low<=x.Close && x.High>=x.Open && x.Low<=x.Open).ToArray();
        var entry=fixedEntry??rows.FirstOrDefault()?.Open;
        var entryDate=fixedEntry.HasValue?(DateOnly?)after:rows.FirstOrDefault()?.Date;
        var corporateAt=int.MaxValue;var index=0;
        decimal? previous=entry;
        foreach(var bar in rows.Take(20)) {index++;if(previous>0 && Math.Abs(bar.Close!.Value/previous.Value-1)>0.25m)corporateAt=Math.Min(corporateAt,index);previous=bar.Close;}
        var status=corporateAt!=int.MaxValue?"價格跳動超過 25%，受影響期間待確認企業行動":all.Length!=rows.Length?"含停牌／無效行情，依有效交易日計算":rows.Length>=20?"20D 已成熟":"等待後續交易日";
        decimal? Return(decimal? price)=>entry>0 && price.HasValue?(price/entry-1)*100:null;
        var horizons=Horizons.Select(n=>
        {
            if(rows.Length<n || corporateAt<=n || entry is null or <=0)return new RadarHorizon(n,null,null,null,null,null);
            var window=rows.Take(n).ToArray();
            return new RadarHorizon(n,window[^1].Date,window[^1].Close,Return(window[^1].Close),Math.Max(0,Return(window.Max(x=>x.High))!.Value),Math.Min(0,Return(window.Min(x=>x.Low))!.Value));
        }).ToArray();
        return new(entryDate,entry,horizons,status);
    }
    public static decimal? Median(IEnumerable<decimal> values)
    {var sorted=values.Order().ToArray();return sorted.Length==0?null:sorted.Length%2==1?sorted[sorted.Length/2]:(sorted[sorted.Length/2-1]+sorted[sorted.Length/2])/2;}
    public static decimal? Correlation((decimal X,decimal Y)[] values)
    {
        if(values.Length<2)return null;var x=values.Average(v=>v.X);var y=values.Average(v=>v.Y);
        var xx=values.Sum(v=>(v.X-x)*(v.X-x));var yy=values.Sum(v=>(v.Y-y)*(v.Y-y));
        return xx==0||yy==0?null:values.Sum(v=>(v.X-x)*(v.Y-y))/(decimal)Math.Sqrt((double)(xx*yy));
    }
}
