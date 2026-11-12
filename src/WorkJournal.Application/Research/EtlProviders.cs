using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkJournal.MarketData;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public interface IDataProviderAdapter
{
    ProviderCapabilities Capabilities {get;}
    ProviderResponse Transform(ProviderRequest request,SourceArchive raw);
}
public sealed class OfficialDataProviderAdapter(string provider):IDataProviderAdapter
{
    public ProviderCapabilities Capabilities=>Catalog().Single(p=>p.Provider==provider);
    public static ProviderCapabilities[] Catalog()=>[
        new("TWSE","SUPPORTED",["Price","Institutional"],"HistoricalNormalized-v1","CONTROLLED_REGISTERED_SOURCE_ONLY","UNPROVEN",["Capture time is not historical availability","Independent reviewer/PIT/approval required"]),
        new("TPEx","SUPPORTED",["Price"],"HistoricalNormalized-v1","CONTROLLED_REGISTERED_SOURCE_ONLY","UNPROVEN",["Monthly price only; rounded lots are not exact shares","Independent reviewer/PIT/approval required"]),
        new("FinMind","MANUAL_ACTION_REQUIRED",[],"HistoricalNormalized-v1","EXPLICIT_AUTHORIZATION_AND_DATA_CONTRACT_REQUIRED","UNPROVEN",["Existing application credentials do not grant a new ETL license; no token exported"])
    ];
    public ProviderResponse Transform(ProviderRequest request,SourceArchive raw)
    {
        Validate(request,raw);var bytes=Convert.FromBase64String(raw.BodyBase64);
        if(bytes.Length>AcquisitionSafety.MaximumBytes)throw new InvalidDataException("ARTIFACT_TOO_LARGE");
        if(Convert.ToHexString(SHA256.HashData(bytes))!=raw.SourceChecksum)throw new InvalidDataException("RAW_DIGEST_MISMATCH");
        if(provider=="TWSE")
        {
            var capture=OfficialHistoricalSource.Parse(raw,request.Date,request.Instrument);
            return new(capture.Facts.ToArray(),raw.ParserVersion,raw.SourceChecksum,raw.SourceUri,raw.RetrievedAt,raw.Synthetic);
        }
        using var document=JsonDocument.Parse(bytes);
        var rows=OfficialParser.MonthlyPrices(document.RootElement,request.Instrument,"tpex",request.Date);
        var facts=rows.Select(row=>{
            var day=DateOnly.ParseExact(row.GetProperty("date").GetString()!,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
            decimal? Number(string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetDecimal(out var n)?n:null;
            var fact=new HistoricalFact("TPEx",HistoricalDatasetType.Price,request.Instrument,day,new(day.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(8)),null,
                raw.ObservedAt,raw.RetrievedAt,"captured-body:"+raw.SourceChecksum,$"TPEx/Price/{request.Instrument}/{day:yyyy-MM-dd}",raw.ArchiveId,
                new(Number("open"),Number("max"),Number("min"),Number("close")),"UNKNOWN_EXACT_SHARES","TWD",EvidenceStatus.Available,
                raw.SourceChecksum,"",null,AvailabilityConfidence.Unknown,"","","TPEx-raw-captured-v1");
            return fact with{FactChecksum=HistoricalAvailability.FactChecksum(fact)};
        }).ToArray();
        return new(facts,"TPEx-monthly-price-only-v1",raw.SourceChecksum,raw.SourceUri,raw.RetrievedAt,raw.Synthetic);
    }
    public void Validate(ProviderRequest r,SourceArchive a)
    {
        if(provider is not ("TWSE" or "TPEx")||r.Provider!=provider||a.Provider!=provider||!Capabilities.Datasets.Contains(r.Dataset)||a.DatasetType.ToString()!=r.Dataset)
            throw new ArgumentException("UNSUPPORTED_PROVIDER_CONTRACT");
        if(r.SchemaVersion!="HistoricalNormalized-v1"||r.TransformCodeSha.Length!=40||!r.TransformCodeSha.All(Uri.IsHexDigit)||r.SourceVersion!=a.SourceChecksum||r.SourceVersion.Length!=64||
            r.CoverageStart>r.CoverageEnd||r.Date<r.CoverageStart||r.Date>r.CoverageEnd||!Regex.IsMatch(r.Instrument,@"^[0-9]{4}[0-9A-Z]{0,2}$"))throw new ArgumentException("SOURCE_VERSION_OR_REQUEST_MISMATCH");
        var url=AcquisitionSafety.ValidateUrl(a.SourceUri);
        var expected=provider=="TWSE"?(r.Dataset=="Price"?"/rwd/zh/afterTrading/STOCK_DAY":"/rwd/zh/fund/T86"):"/www/zh-tw/afterTrading/tradingStock";
        if(url.Host!=(provider=="TWSE"?"www.twse.com.tw":"www.tpex.org.tw")||url.AbsolutePath!=expected||a.ParserVersion!=provider+"-capture-v1")throw new ArgumentException("UNSUPPORTED_SOURCE_SCHEMA");
        var query=url.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Split('=',2)).ToDictionary(x=>x[0],x=>x.Length==2?Uri.UnescapeDataString(x[1]):"");
        var symbolKey=provider=="TWSE"?"stockNo":"code";
        if(r.Dataset=="Price"&&(!query.TryGetValue(symbolKey,out var symbol)||symbol!=r.Instrument))throw new ArgumentException("INSTRUMENT_REQUEST_MISMATCH");
        if(!query.TryGetValue("date",out var date)||OfficialParser.Date(date)!=r.Date)throw new ArgumentException("DATE_REQUEST_MISMATCH");
        if(a.RetrievedAt<a.ObservedAt)throw new InvalidDataException("CAPTURE_TIME_CONFLICT");
    }
}
public static class ProviderErrorClassification
{
    public static string Classify(Exception e)=>e switch{
        OperationCanceledException=>"CANCELLED",HttpRequestException=>"PROVIDER_FAILURE",JsonException=>"SCHEMA_DRIFT",
        InvalidDataException=>"DATA_OR_DIGEST_INVALID",ArgumentException=>"INVALID_PROVIDER_REQUEST",IOException=>"STORAGE_FAILURE",_=>"TRANSFORM_FAILED"};
}
