using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkJournal.MarketData;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

// Acquisition does not certify historical publication/revision evidence.
public sealed class OfficialHistoricalSource(HttpClient client,TimeProvider clock)
{
    public async Task<DatasetCapture> CaptureAsync(HistoricalDatasetType kind,DateOnly date,string symbol,CancellationToken cancellationToken=default)
    {
        if(kind is not (HistoricalDatasetType.Price or HistoricalDatasetType.Institutional)||!Regex.IsMatch(symbol,@"^[0-9]{4}[0-9A-Z]{0,2}$"))
            throw new ArgumentException("TWSE price or institutional dataset and explicit symbol required.");
        var endpoint=kind==HistoricalDatasetType.Price?$"afterTrading/STOCK_DAY?date={date:yyyyMMdd}&stockNo={symbol}&response=json":$"fund/T86?date={date:yyyyMMdd}&selectType=ALLBUT0999&response=json";
        var url="https://www.twse.com.tw/rwd/zh/"+endpoint;
        using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        response.EnsureSuccessStatusCode();var observed=clock.GetUtcNow();
        await response.Content.LoadIntoBufferAsync(16*1024*1024,cancellationToken);
        var bytes=await response.Content.ReadAsByteArrayAsync(cancellationToken);var retrieved=clock.GetUtcNow();
        var checksum=Convert.ToHexString(SHA256.HashData(bytes));
        var archive=new SourceArchive(ResearchMath.Digest(new{url,observed,retrieved,checksum}),"TWSE",kind,url,observed,retrieved,
            Convert.ToBase64String(bytes),checksum,"TWSE-capture-v1",false);
        return Parse(archive,date,symbol);
    }
    public static DatasetCapture Parse(SourceArchive archive,DateOnly date,string symbol)
    {
        if(archive.Provider!="TWSE"||archive.ParserVersion!="TWSE-capture-v1")throw new ArgumentException("Unsupported archive parser.");
        var bytes=Convert.FromBase64String(archive.BodyBase64);
        if(Convert.ToHexString(SHA256.HashData(bytes))!=archive.SourceChecksum)throw new InvalidDataException("Archive checksum mismatch.");
        using var doc=JsonDocument.Parse(bytes);
        var facts=new List<HistoricalFact>();
        void Add(DateOnly day,HistoricalValue value)
        {
            // EffectiveAt denotes the economic period only; NEVER a publication-time substitute.
            var effective=new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(8));
            var f=new HistoricalFact("TWSE",archive.DatasetType,symbol,day,effective,null,archive.ObservedAt,archive.RetrievedAt,
                "captured-body:"+archive.SourceChecksum,$"TWSE/{archive.DatasetType}/{symbol}/{day:yyyy-MM-dd}",archive.ArchiveId,
                value,"Shares","TWD",EvidenceStatus.Available,archive.SourceChecksum,"",null,AvailabilityConfidence.Unknown,"","","TWSE-raw-captured-v1");
            facts.Add(f with{FactChecksum=HistoricalAvailability.FactChecksum(f)});
        }
        decimal? Number(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetDecimal(out var d)?d:null;
        if(archive.DatasetType==HistoricalDatasetType.Price)
        {
            foreach(var row in OfficialParser.MonthlyPrices(doc.RootElement,symbol,"twse",date))
                Add(DateOnly.ParseExact(row.GetProperty("date").GetString()!,"yyyy-MM-dd",CultureInfo.InvariantCulture),
                    new(Number(row,"open"),Number(row,"max"),Number(row,"min"),Number(row,"close"),Number(row,"Trading_Volume"),Number(row,"Trading_money")));
        }
        else if(archive.DatasetType==HistoricalDatasetType.Institutional)
        {
            var rows=OfficialParser.Flows(doc.RootElement,"twse",date).Where(x=>x.GetProperty("stock_id").GetString()==symbol).ToArray();
            decimal? Net(string group)
            {
                var matches=rows.Where(x=>x.GetProperty("name").GetString()==group).ToArray();
                return matches.Length==1?Number(matches[0],"buy")-Number(matches[0],"sell"):null;
            }
            if(rows.Length>0)Add(date,new(Foreign:Net("Foreign_Investor"),Trust:Net("Investment_Trust"),Dealer:Net("Dealer_self")+Net("Dealer_Hedging")));
        }
        else throw new ArgumentException("Unsupported capture dataset.");
        return new(archive,facts.ToImmutableArray(),[
            new("AVAILABILITY_GAP","Current historical response does not prove original publication time; AvailableAt remains null."),
            new("DATA_GAP","Captured-body revision identifies downloaded bytes, not an independently verified historical source revision."),
            new("CORPORATE_ACTION_GAP","Raw captured prices are not an approved adjusted/total-return corporate-action series.")]);
    }
}
