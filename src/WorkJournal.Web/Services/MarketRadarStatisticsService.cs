using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Services;
public class MarketRadarStatisticsService(JournalDbContext db,TimeProvider clock)
{
    public static string Bucket(int? score)=>score switch{null=>"評分不完整",<70=>"低分補充",<=74=>"70–74",<=79=>"75–79",<=84=>"80–84",<=89=>"85–89",<=94=>"90–94",_=>"95–100"};
    public IQueryable<MarketRadarRecommendation> Query(RadarTrackingFilter f,string owner)
    {
        f.From??=MarketRadarRecommendationService.TaipeiDate(clock.GetUtcNow()).AddDays(-90);
        f.To??=MarketRadarRecommendationService.TaipeiDate(clock.GetUtcNow());
        f.Industry??="";f.Symbol??="";
        if(f.Symbol.Length>6||f.Industry.Length>80||f.ScoreMin is <0 or >100||f.ScoreMax is <0 or >100||f.ScoreMin>f.ScoreMax)throw new System.ComponentModel.DataAnnotations.ValidationException("代號、產業或分數範圍不正確。");
        if(f.From>f.To || f.To.Value.DayNumber-f.From.Value.DayNumber>3660)throw new System.ComponentModel.DataAnnotations.ValidationException("日期範圍不正確，最多查詢 10 年。");
        f.Page=Math.Max(1,f.Page);f.PageSize=f.PageSize is 20 or 50 or 100?f.PageSize:20;
        var q=db.MarketRadarRecommendations.AsNoTracking().Where(x=>x.TradeDate>=f.From&&x.TradeDate<=f.To);
        q=f.Mine?q.Where(x=>db.MarketRadarPersonalTrackings.Any(t=>t.RecommendationId==x.Id&&t.ApplicationUserId==owner)):
            q.Where(x=>db.MarketRadarDailySelections.Any(d=>d.RecommendationId==x.Id));
        if(f.Market=="etf")q=q.Where(x=>x.IsEtf);
        else if(f.Market is "twse" or "tpex")q=q.Where(x=>x.Market==f.Market&&!x.IsEtf);
        if(f.Category is "Qualified" or "Incomplete" or "Supplement")q=q.Where(x=>x.Category==f.Category);
        if(!string.IsNullOrWhiteSpace(f.Industry))q=q.Where(x=>x.Industry==f.Industry);
        if(!string.IsNullOrWhiteSpace(f.Symbol))q=q.Where(x=>x.StockId.Contains(f.Symbol));
        if(f.ScoreMin.HasValue)q=q.Where(x=>x.Score>=f.ScoreMin);
        if(f.ScoreMax.HasValue)q=q.Where(x=>x.Score<=f.ScoreMax);
        if(f.Result=="win")q=q.Where(x=>x.Performance!=null&&(f.Basis=="open"?x.Performance.OpenReturn20D:x.Performance.Return20D)>0);
        if(f.Result=="loss")q=q.Where(x=>x.Performance!=null&&(f.Basis=="open"?x.Performance.OpenReturn20D:x.Performance.Return20D)<0);
        if(f.Result=="pending")q=q.Where(x=>x.Performance==null||(f.Basis=="open"?x.Performance.OpenReturn20D:x.Performance.Return20D)==null);
        return q;
    }
    public async Task<RadarTrackingViewModel> TrackingAsync(RadarTrackingFilter f,string owner,CancellationToken ct)
    {
        var q=Query(f,owner);var total=await q.CountAsync(ct);f.Page=Math.Min(f.Page,Math.Max(1,(total+f.PageSize-1)/f.PageSize));
        var rows=await q.Include(x=>x.Performance).OrderByDescending(x=>x.TradeDate).ThenBy(x=>x.Id).Skip((f.Page-1)*f.PageSize).Take(f.PageSize).ToListAsync(ct);
        var ids=rows.Select(x=>x.Id).ToArray();var personal=await db.MarketRadarPersonalTrackings.AsNoTracking().Where(x=>x.ApplicationUserId==owner&&ids.Contains(x.RecommendationId)).ToDictionaryAsync(x=>x.RecommendationId,ct);
        return new(f,rows.Select(x=>new RadarTrackingItem(x,x.Performance,personal.GetValueOrDefault(x.Id))).ToList(),total);
    }
    public async Task<RadarPerformanceViewModel> StatisticsAsync(RadarTrackingFilter f,string owner,CancellationToken ct)
    {
        var q=Query(f,owner);
        // Load only numeric projections for median/correlation; no snapshot/entity graph is materialized.
        var numbers=await q.Select(x=>new Numeric {Score=x.Score,Industry=x.Industry,Category=x.Category,ForeignStreak=x.ForeignBuyStreak,
            R1=x.Performance==null?null:f.Basis=="open"?x.Performance.OpenReturn1D:x.Performance.Return1D,
            R3=x.Performance==null?null:f.Basis=="open"?x.Performance.OpenReturn3D:x.Performance.Return3D,
            R5=x.Performance==null?null:f.Basis=="open"?x.Performance.OpenReturn5D:x.Performance.Return5D,
            R10=x.Performance==null?null:f.Basis=="open"?x.Performance.OpenReturn10D:x.Performance.Return10D,
            R20=x.Performance==null?null:f.Basis=="open"?x.Performance.OpenReturn20D:x.Performance.Return20D,
            Mfe=f.Basis=="open"||x.Performance==null?null:x.Performance.Mfe20D,Mae=f.Basis=="open"||x.Performance==null?null:x.Performance.Mae20D,
            Momentum=x.MomentumPoints,Volume=x.VolumePoints,Technical=x.TechnicalPoints,Chip=x.ChipPoints,Fundamental=x.FundamentalPoints,Event=x.EventPoints}).ToListAsync(ct);
        async Task<List<RadarStatisticsRow>> Aggregate(bool industry)
        {
            var groups=await q.Select(x=>new{Group=industry?x.Industry+" / "+x.Category:x.Category=="Incomplete"?"評分不完整":x.Category=="Supplement"?"低分補充":x.Score>=85?"85–100":"70–84",
                R5=x.Performance==null?null:f.Basis=="open"?x.Performance.OpenReturn5D:x.Performance.Return5D,
                R20=x.Performance==null?null:f.Basis=="open"?x.Performance.OpenReturn20D:x.Performance.Return20D})
                .GroupBy(x=>x.Group).Select(g=>new{Group=g.Key,Count=g.Count(),N5=g.Count(x=>x.R5!=null),N20=g.Count(x=>x.R20!=null),W5=g.Count(x=>x.R5>0),W20=g.Count(x=>x.R20>0),A5=g.Average(x=>x.R5),A20=g.Average(x=>x.R20)}).ToListAsync(ct);
            return groups.Select(g=>{var ns=numbers.Where(n=>(industry?n.Industry+" / "+n.Category:n.Category=="Incomplete"?"評分不完整":n.Category=="Supplement"?"低分補充":n.Score>=85?"85–100":"70–84")==g.Group).ToArray();return new RadarStatisticsRow(g.Group.Replace("Qualified","完整 ≥70").Replace("Incomplete","評分不完整").Replace("Supplement","低分補充"),g.Count,g.N5,g.N20,g.N5==0?null:g.W5*100m/g.N5,g.N20==0?null:g.W20*100m/g.N20,g.A5,g.A20,Median(ns.Select(n=>n.R5)),Median(ns.Select(n=>n.R20)),Average(ns.Select(n=>n.Mfe)),Average(ns.Select(n=>n.Mae)),ns.Average(n=>(decimal?)n.Score));}).ToList();
        }
        var main=await Aggregate(false);var industries=await Aggregate(true);
        var validation=numbers.GroupBy(n=>Bucket(n.Score)).OrderBy(g=>g.Key).Select(g=>new RadarValidationRow(g.Key,g.Count(),Enumerable.Range(0,5).Select(i=>Win(g.Select(n=>n.Returns[i]))).ToArray(),Enumerable.Range(0,5).Select(i=>Average(g.Select(n=>n.Returns[i]))).ToArray(),Enumerable.Range(0,5).Select(i=>g.Count(n=>n.Returns[i].HasValue)).ToArray(),Median(g.Select(n=>n.R5)),Median(g.Select(n=>n.R20)),Average(g.Select(n=>n.Mfe)),Average(g.Select(n=>n.Mae)))).ToList();
        var components=new List<RadarComponentRow>();var names=new[]{"價格動能","成交量異常","技術面","籌碼面","基本面","重大資訊"};var max=new[]{20,20,20,15,10,15};
        foreach(var category in numbers.GroupBy(x=>x.Category))
        {
        var categoryLabel=category.Key=="Qualified"?"完整 ≥70":category.Key=="Incomplete"?"評分不完整":"低分補充";
        for(int i=0;i<6;i++)foreach(var high in new[]{true,false})
        {
            var rows=category.Where(n=>n.Points[i].HasValue&&(n.Points[i]>=max[i]/2m)==high).ToArray();
            components.Add(new(names[i],categoryLabel+" / "+(high?"高分（至少一半配分）":"低分"),rows.Length,Win(rows.Select(x=>x.R5)),Win(rows.Select(x=>x.R20)),Average(rows.Select(x=>x.R5)),Average(rows.Select(x=>x.R20)),rows.Count(x=>x.R5.HasValue),rows.Count(x=>x.R20.HasValue)));
        }
        foreach(var label in new[]{"連買≥3日","未達3日","資料不足"})
        {
            var rows=category.Where(n=>(n.ForeignStreak==null?"資料不足":n.ForeignStreak>=3?"連買≥3日":"未達3日")==label).ToArray();
            components.Add(new("外資",categoryLabel+" / "+label,rows.Length,Win(rows.Select(x=>x.R5)),Win(rows.Select(x=>x.R20)),Average(rows.Select(x=>x.R5)),Average(rows.Select(x=>x.R20)),rows.Count(x=>x.R5.HasValue),rows.Count(x=>x.R20.HasValue)));
        }
        }
        return new(f,main,industries,validation,components,
            MarketRadarPerformanceCalculator.Correlation(numbers.Where(x=>x.Score.HasValue&&x.R5.HasValue).Select(x=>((decimal)x.Score!.Value,x.R5!.Value)).ToArray()),
            MarketRadarPerformanceCalculator.Correlation(numbers.Where(x=>x.Score.HasValue&&x.R20.HasValue).Select(x=>((decimal)x.Score!.Value,x.R20!.Value)).ToArray()));
    }
    public static decimal? Win(IEnumerable<decimal?> values){var a=values.Where(x=>x.HasValue).Select(x=>x!.Value).ToArray();return a.Length==0?null:a.Count(x=>x>0)*100m/a.Length;}
    private static decimal? Average(IEnumerable<decimal?> values)=>values.Average();
    private static decimal? Median(IEnumerable<decimal?> values)=>MarketRadarPerformanceCalculator.Median(values.Where(x=>x.HasValue).Select(x=>x!.Value));
    private class Numeric
    {
        public int? Score{get;set;}public string Industry{get;set;}="";public string Category{get;set;}="";public int? ForeignStreak{get;set;}
        public decimal? R1{get;set;}public decimal? R3{get;set;}public decimal? R5{get;set;}public decimal? R10{get;set;}public decimal? R20{get;set;}public decimal? Mfe{get;set;}public decimal? Mae{get;set;}
        public int? Momentum{get;set;}public int? Volume{get;set;}public int? Technical{get;set;}public int? Chip{get;set;}public int? Fundamental{get;set;}public int? Event{get;set;}
        public decimal?[] Returns=>[R1,R3,R5,R10,R20];public int?[] Points=>[Momentum,Volume,Technical,Chip,Fundamental,Event];
    }
}
