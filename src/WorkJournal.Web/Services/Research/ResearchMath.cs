using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Evidence;
namespace WorkJournal.Web.Services.Research;

public static class ResearchMath
{
    public static decimal? Mean(IEnumerable<decimal> values) { var a=values.ToArray(); return a.Length==0?null:a.Average(); }
    public static decimal? Median(IEnumerable<decimal> values) { var a=values.ToArray(); return a.Length==0?null:CandidateMath.Median(a); }
    public static decimal? StdDev(IEnumerable<decimal> values)
    {
        var a=values.ToArray(); if(a.Length==0)return null;
        var mean=a.Average(); return CandidateMath.Sqrt(a.Sum(x=>(x-mean)*(x-mean))/a.Length);
    }
    public static decimal[] Ranks(IReadOnlyList<decimal> values) => values.Select(x=>1m+values.Count(v=>v<x)+(values.Count(v=>v==x)-1)/2m).ToArray();
    public static decimal? Correlation(IReadOnlyList<decimal> x,IReadOnlyList<decimal> y)
    {
        if(x.Count!=y.Count)throw new ArgumentException("Paired observations required.");
        if(x.Count<2)return null;
        var mx=x.Average();var my=y.Average();
        var sx=x.Sum(v=>(v-mx)*(v-mx));var sy=y.Sum(v=>(v-my)*(v-my));
        if(sx==0 || sy==0)return null;
        return Math.Clamp(x.Select((v,i)=>(v-mx)*(y[i]-my)).Sum()/CandidateMath.Sqrt(sx)/CandidateMath.Sqrt(sy),-1m,1m);
    }
    public static decimal? RankIc(IReadOnlyList<decimal> x,IReadOnlyList<decimal> y)=>Correlation(Ranks(x),Ranks(y));
    public static int[] Quantiles(IReadOnlyList<decimal> values,int count)
    {
        if(count<2)throw new ArgumentOutOfRangeException(nameof(count));
        if(values.Count==0)return [];
        // All ties stay together; empty quantiles remain empty, never filled artificially.
        return Ranks(values).Select(r=>Math.Min(count,1+(int)decimal.Floor((r-.5m)/values.Count*count))).ToArray();
    }
    public static decimal? Percentile(IEnumerable<decimal> values,decimal p)
    {
        if(p<0 || p>1)throw new ArgumentOutOfRangeException(nameof(p));
        var a=values.Order().ToArray();if(a.Length==0)return null;
        var position=p*(a.Length-1);var lo=(int)decimal.Floor(position);var hi=(int)decimal.Ceiling(position);
        return a[lo]+(a[hi]-a[lo])*(position-lo);
    }
    public static ResearchDistribution Distribution(IEnumerable<decimal?> input)
    {
        var all=input.ToArray();var a=all.Where(x=>x.HasValue).Select(x=>x!.Value).ToArray();
        decimal? P(decimal p)=>Percentile(a,p);
        return new(a.Length,P(0),P(.01m),P(.05m),P(.10m),P(.25m),P(.5m),P(.75m),P(.9m),P(.95m),P(.99m),P(1),Mean(a),StdDev(a),all.Length==0?0:1m-(decimal)a.Length/all.Length);
    }
    public static string Canonical<T>(T value)
    {
        using var document=JsonDocument.Parse(JsonSerializer.Serialize(value));
        using var stream=new MemoryStream();using(var writer=new Utf8JsonWriter(stream))Write(writer,document.RootElement);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static void Write(Utf8JsonWriter w,JsonElement e)
    {
        switch(e.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();foreach(var p in e.EnumerateObject().OrderBy(x=>x.Name,StringComparer.Ordinal)){w.WritePropertyName(p.Name);Write(w,p.Value);}w.WriteEndObject();break;
            case JsonValueKind.Array:w.WriteStartArray();foreach(var v in e.EnumerateArray())Write(w,v);w.WriteEndArray();break;
            case JsonValueKind.Number:w.WriteRawValue(e.GetDecimal().ToString("G29",CultureInfo.InvariantCulture));break;
            default:e.WriteTo(w);break;
        }
    }
    public static string Digest<T>(T value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(value))));
}
