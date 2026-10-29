using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkJournal.Web.Models;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.V2;
namespace WorkJournal.Web.Services.Evidence;

// Pure research engine. No current-universe fallback, provider calls, DB writes, or trading decisions.
public sealed class V3CandidateCalculator
{
    public const string Version = "V3EvidenceCandidate-v1";
    public static string ParameterChecksum(V3EvidenceCandidateV1ParameterSet p) => Hash(JsonSerializer.Serialize(p));
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private sealed class Gap(EvidenceStatus status, string reason) : Exception(reason)
    { public EvidenceStatus Status { get; } = status; }
    private static Gap Definition(string name) => new(EvidenceStatus.Missing, "MODEL_DEFINITION_GAP:"+name);
    private static Gap Missing(string name) => new(EvidenceStatus.Missing, "PIT_DATA_GAP:"+name);
    private static Gap Invalid(string name) => new(EvidenceStatus.Invalid, "INVALID:"+name);
    private static int Period(int? n, string name) => n is > 0 ? n.Value : throw Definition(name);
    private static LinearBounds Bounds(LinearBounds? b, string name) => b is null ? throw Definition(name) :
        b.Upper > b.Lower ? b : throw Invalid(name);
    private static decimal Divide(decimal numerator, decimal denominator, string name) => denominator > 0 ? numerator/denominator : throw Invalid(name);
    private static decimal Weighted(IReadOnlyList<decimal> values, ImmutableArray<decimal> weights) => values.Select((v,i) => v*weights[i]).Sum();

    public CandidateTrace Calculate(CandidateDataset input, Guid instrumentId, V3EvidenceCandidateV1ParameterSet p)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(p);
        ValidateParameters(p);
        var id = input.Identity;
        ArgumentNullException.ThrowIfNull(id);
        if (input.Series.IsDefault || input.Benchmark.IsDefault || input.Series.Any(s => s.Bars.IsDefault || s.Flows.IsDefault))
            throw new ArgumentException("Explicit arrays required; use empty arrays for missing data.");
        if (new[] { id.DatasetSnapshotId,id.UniverseSnapshotId,id.BenchmarkVersionId,id.VersionSetId,id.ParameterSetId }.Any(x => x == Guid.Empty) ||
            new[] { id.DatasetChecksum,id.UniverseChecksum,id.BenchmarkChecksum,id.PriceBasisVersion }.Any(string.IsNullOrWhiteSpace) ||
            id.ParameterChecksum != ParameterChecksum(p) || input.Status != FoundationStatus.Finalized)
            throw new ArgumentException("Finalized, versioned snapshot references and matching parameter checksum required.");
        if (input.UniverseMembers.IsDefaultOrEmpty || input.UniverseMembers.Any(x => x == Guid.Empty) ||
            input.UniverseMembers.Distinct().Count() != input.UniverseMembers.Length || !input.UniverseMembers.Contains(instrumentId))
            throw new ArgumentException("Explicit historical universe membership required.");
        if (input.Sessions.IsDefaultOrEmpty || input.Sessions.Distinct().Count() != input.Sessions.Length)
            throw new ArgumentException("Unique historical calendar required.");
        var sessions = input.Sessions.Where(x => x <= id.TradeDate).Order().ToImmutableArray();
        if (sessions.IsEmpty || sessions[^1] != id.TradeDate) throw new ArgumentException("Trade date absent from historical calendar.");
        if (input.Series.Select(x => x.InstrumentId).Distinct().Count() != input.Series.Length || input.Series.Any(x => !input.UniverseMembers.Contains(x.InstrumentId)))
            throw new ArgumentException("Duplicate or non-universe series.");
        bool Visible(DateOnly date, DateTimeOffset? available) => date <= id.TradeDate && (!available.HasValue || available <= id.DataCutoff);
        // Snapshot detached from caller; future rows/revisions cannot alter historical calculations/digest.
        var data = input with {
            Sessions = sessions,
            UniverseMembers = input.UniverseMembers.Order().ToImmutableArray(),
            Series = input.Series.OrderBy(x => x.InstrumentId).Select(s => s with {
                Bars = s.Bars.Where(b => Visible(b.Date,b.AvailableAt)).OrderBy(b => b.Date).ToImmutableArray(),
                Flows = s.Flows.Where(f => Visible(f.Date,f.AvailableAt)).OrderBy(f => f.Date).ToImmutableArray()
            }).ToImmutableArray(),
            Benchmark = input.Benchmark.Where(b => Visible(b.Date,b.AvailableAt)).OrderBy(b => b.Date).ToImmutableArray()
        };
        var ctx = new Context(data, p);
        var components = Enum.GetValues<EvidenceCode>().Select(code => Compute(ctx,instrumentId,code)).ToImmutableArray();
        var score = new FullEvidenceScoreCalculator().Calculate(components.Select(x => x.Evidence));
        var digest = Hash(JsonSerializer.Serialize(data));
        var identity = Hash(JsonSerializer.Serialize(new { id, instrumentId, CandidateVersion = Version, InputDigest = digest }));
        return new(id,instrumentId,Version,identity,digest,score,components);
    }

    private static void ValidateParameters(V3EvidenceCandidateV1ParameterSet p)
    {
        var spec = new V3EvidenceCandidateV1ParameterSet();
        bool Same<T>(ImmutableArray<T> a, ImmutableArray<T> b) => !a.IsDefault && a.SequenceEqual(b);
        // A changed formula needs a separate version/implementation; no silent candidate-v1 edits.
        if (p.CandidateVersion != Version || p.MaShort != 20 || p.MaMedium != 60 || p.MaLong != 120 ||
            p.FlowDenominator != spec.FlowDenominator || p.PercentileMethod != spec.PercentileMethod || (p.LiquidityPrice is not null and not "Close") ||
            !Same(p.TrendWeights,spec.TrendWeights) || !Same(p.DistanceWeights,spec.DistanceWeights) ||
            !Same(p.MomentumWeights,spec.MomentumWeights) || !Same(p.RsiAnchors,spec.RsiAnchors) ||
            !Same(p.VolumePriceWeights,spec.VolumePriceWeights) || !Same(p.InstitutionalWeights,spec.InstitutionalWeights) ||
            !Same(p.InstitutionalHorizonWeights,spec.InstitutionalHorizonWeights) ||
            !Same(p.RelativeStrengthWeights,spec.RelativeStrengthWeights) || !Same(p.RiskWeights,spec.RiskWeights))
            throw new ArgumentException("Candidate v1 formula is locked. Formula changes require a new candidate version.");
    }

    private static CandidateComponent Compute(Context c, Guid instrument, EvidenceCode code)
    {
        var features = new List<CandidateFeature>();
        decimal F(string name, decimal raw, decimal normalized) { features.Add(new(name,raw,normalized)); return normalized; }
        decimal L(string name, decimal raw, LinearBounds? bounds) => F(name,raw,CandidateMath.Linear(raw,Bounds(bounds,name)));
        try
        {
            c.Availability(c.Data.UniverseAvailableAt, "UNIVERSE_AVAILABILITY");
            var p = c.P;
            decimal value;
            switch (code)
            {
                case EvidenceCode.Trend:
                    var slope = Period(p.SlopeHorizon,"SlopeHorizon");
                    var structure = Period(p.StructureHorizon,"StructureHorizon");
                    var bars = c.Bars(instrument, Math.Max(p.MaLong,Math.Max(p.MaMedium+slope,structure+1)));
                    decimal Ma(int n, int lag = 0) => bars.Take(bars.Length-lag).TakeLast(n).Average(b => b.Close);
                    var shortMa = Ma(p.MaShort); var midMa = Ma(p.MaMedium); var longMa = Ma(p.MaLong);
                    var alignment = ((shortMa-midMa)/midMa+(midMa-longMa)/longMa)/2;
                    var position = Weighted([(bars[^1].Close-shortMa)/shortMa,(bars[^1].Close-midMa)/midMa],p.DistanceWeights);
                    var rawSlope = Weighted([shortMa/Ma(p.MaShort,slope)-1,midMa/Ma(p.MaMedium,slope)-1],p.DistanceWeights);
                    var prior = bars.SkipLast(1).TakeLast(structure).ToArray();
                    var low = prior.Min(b => b.Low); var high = prior.Max(b => b.High);
                    var rangePosition = Divide(bars[^1].Close-low,high-low,"ZERO_STRUCTURE_RANGE");
                    value = Weighted([L("Trend.Alignment",alignment,p.AlignmentBounds),L("Trend.Position",position,p.PositionBounds),
                        L("Trend.Slope",rawSlope,p.SlopeBounds),F("Trend.Structure",rangePosition,CandidateMath.Clamp01(rangePosition))],p.TrendWeights);
                    break;
                case EvidenceCode.Momentum:
                    var rocHorizon = Period(p.RocHorizon,"RocHorizon");
                    // Reuse unchanged repository Wilder RSI14 / seeded EMA12,26,9 definitions.
                    var history = c.AllBars(instrument,Math.Max(34,rocHorizon+1));
                    var technical = TechnicalIndicators.Calculate(history.Select(b => new DailyClose(b.Date,b.Close)));
                    if (technical.RSI14 is null || technical.Histogram is null) throw Missing("INDICATOR_WARMUP");
                    var rsi = technical.RSI14.Value;
                    value = Weighted([F("Momentum.RSI",rsi,CandidateMath.Piecewise(rsi,p.RsiAnchors)),
                        L("Momentum.MACD",technical.Histogram.Value/history[^1].Close,p.MacdBounds),
                        L("Momentum.ROC",history[^1].Close/history[^(rocHorizon+1)].Close-1,p.RocBounds)],p.MomentumWeights);
                    break;
                case EvidenceCode.VolumePrice:
                    var vn = Period(p.VolumeHorizon,"VolumeHorizon"); var rn = Period(p.ReturnHorizon,"ReturnHorizon"); var bn = Period(p.BreakoutHorizon,"BreakoutHorizon");
                    var vb = c.Bars(instrument,Math.Max(vn,Math.Max(rn,bn))+1);
                    var rvol = Divide(vb[^1].Volume,vb.SkipLast(1).TakeLast(vn).Average(b => b.Volume),"ZERO_PRIOR_VOLUME");
                    if (rvol <= 0) throw Invalid("LOG_NONPOSITIVE_RVOL");
                    var log = CandidateMath.Log(rvol);
                    var pvc = (vb[^1].Close/vb[^(rn+1)].Close-1)*log;
                    var priorHigh = vb.SkipLast(1).TakeLast(bn).Max(b => b.High);
                    value = Weighted([L("VolumePrice.RVOLLog",log,p.RvolLogBounds),L("VolumePrice.PVC",pvc,p.PvcBounds),
                        L("VolumePrice.Breakout",vb[^1].Close/priorHigh-1,p.BreakoutBounds)],p.VolumePriceWeights);
                    break;
                case EvidenceCode.Institutional:
                    var rawFlow = c.Flow(instrument);
                    var foreign = c.Rank(instrument,rawFlow[0],s => c.Flow(s)[0]);
                    var trust = c.Rank(instrument,rawFlow[1],s => c.Flow(s)[1]);
                    var dealer = c.Rank(instrument,rawFlow[2],s => c.Flow(s)[2]);
                    var pn = Period(p.PersistenceHorizon,"PersistenceHorizon");
                    if (p.Persistence is null || !Enum.IsDefined(p.Persistence.Value)) throw Definition("PersistenceGroup");
                    var flows = c.Flows(instrument,pn);
                    // Validate each persistence session, including trading-volume quality.
                    c.Bars(instrument,pn);
                    var ratio = (decimal)flows.Count(f => (p.Persistence switch {
                        PersistenceGroup.Foreign => f.Foreign, PersistenceGroup.Trust => f.Trust,
                        PersistenceGroup.Dealer => f.Dealer, _ => f.Foreign+f.Trust+f.Dealer }) > 0)/pn;
                    value = Weighted([F("Institutional.Foreign",rawFlow[0],foreign),F("Institutional.Trust",rawFlow[1],trust),
                        F("Institutional.Dealer",rawFlow[2],dealer),F("Institutional.Persistence",ratio,ratio)],p.InstitutionalWeights);
                    break;
                case EvidenceCode.RelativeStrength:
                    var rs = c.RelativeStrength(instrument);
                    value = F("RelativeStrength.ExcessReturn",rs,c.Rank(instrument,rs,c.RelativeStrength));
                    break;
                case EvidenceCode.RiskQuality:
                    var risk = c.Risk(instrument);
                    value = Weighted(Enumerable.Range(0,4).Select(i => F(new[] { "Risk.Volatility","Risk.Drawdown","Risk.ATR","Risk.Liquidity" }[i],risk[i],
                        i == 3 ? c.Rank(instrument,risk[i],s => c.Risk(s)[i]) : 1-c.Rank(instrument,risk[i],s => c.Risk(s)[i]))).ToArray(),p.RiskWeights);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(code));
            }
            return new(new(code,value,EvidenceStatus.Available,"CANDIDATE_ONLY",Version,Version),features.ToImmutableArray());
        }
        catch (Gap e) { return new(new(code,null,e.Status,e.Message,Version,Version),[]); }
        catch (OverflowException) { return new(new(code,null,EvidenceStatus.Invalid,"NUMERIC_OVERFLOW",Version,Version),[]); }
    }

    private sealed class Context(CandidateDataset data, V3EvidenceCandidateV1ParameterSet p)
    {
        public CandidateDataset Data { get; } = data;
        public V3EvidenceCandidateV1ParameterSet P { get; } = p;
        public void Availability(DateTimeOffset? at, string name)
        { if (at is null || at > Data.Identity.DataCutoff) throw Missing(name); }
        private void Quality(EvidenceStatus status)
        {
            if (!Enum.IsDefined(status)) throw Invalid("UNKNOWN_STATUS");
            if (status != EvidenceStatus.Available) throw new Gap(status,"INPUT_"+status.ToString().ToUpperInvariant());
        }
        private CandidateSeries Series(Guid id) => Data.Series.SingleOrDefault(x => x.InstrumentId == id) ?? throw Missing("UNIVERSE_SERIES:"+id);
        private CandidateBar[] SelectBars(ImmutableArray<CandidateBar> source, int count)
        {
            if (count <= 0 || Data.Sessions.Length < count) throw Missing("WARMUP");
            return Data.Sessions.TakeLast(count).Select(date => {
                var matches = source.Where(b => b.Date == date).ToArray();
                if (matches.Length == 0) throw Missing("BAR:"+date);
                if (matches.Length != 1) throw Invalid("DUPLICATE_BAR:"+date);
                var b = matches[0]; Availability(b.AvailableAt,"BAR_AVAILABILITY:"+date); Quality(b.Status);
                if (b.Close <= 0 || b.Low <= 0 || b.High < b.Low || b.Close < b.Low || b.Close > b.High || b.Volume < 0) throw Invalid("OHLCV:"+date);
                return b;
            }).ToArray();
        }
        public CandidateBar[] Bars(Guid id, int n) => SelectBars(Series(id).Bars,n);
        public CandidateBar[] AllBars(Guid id, int minimum)
        {
            var s = Series(id);
            if (s.Bars.IsEmpty) throw Missing("BARS");
            // Seeded indicators consume the complete explicitly supplied history, including every session.
            var count = Data.Sessions.Count(d => d >= s.Bars[0].Date);
            if (count < minimum) throw Missing("INDICATOR_WARMUP");
            return SelectBars(s.Bars,count);
        }
        public CandidateFlow[] Flows(Guid id, int n)
        {
            if (Data.Sessions.Length < n) throw Missing("FLOW_WARMUP");
            var source = Series(id).Flows;
            return Data.Sessions.TakeLast(n).Select(date => {
                var matches = source.Where(f => f.Date == date).ToArray();
                if (matches.Length == 0) throw Missing("FLOW:"+date);
                if (matches.Length != 1) throw Invalid("DUPLICATE_FLOW:"+date);
                var f = matches[0]; Availability(f.AvailableAt,"FLOW_AVAILABLE_AT_UNVERIFIABLE:"+date); Quality(f.Status); return f;
            }).ToArray();
        }
        private static int[] Horizons(ImmutableArray<int> horizons,string name)
        {
            if (horizons.IsDefault || horizons.Length != 3 || horizons.Any(n => n <= 0) || horizons[0] >= horizons[1] || horizons[1] >= horizons[2]) throw Definition(name);
            return horizons.ToArray();
        }
        public decimal[] Flow(Guid id)
        {
            var horizons = Horizons(P.InstitutionalHorizons,"InstitutionalHorizons");
            var bars = Bars(id,horizons.Max()); var flows = Flows(id,horizons.Max());
            return Enumerable.Range(0,3).Select(group => Weighted(horizons.Select(n => Divide(
                flows.TakeLast(n).Sum(f => group == 0 ? f.Foreign : group == 1 ? f.Trust : f.Dealer),
                bars.TakeLast(n).Sum(b => b.Volume),"ZERO_FLOW_DENOMINATOR")).ToArray(),P.InstitutionalHorizonWeights)).ToArray();
        }
        public decimal RelativeStrength(Guid id)
        {
            Availability(Data.BenchmarkMappingAvailableAt,"BENCHMARK_MAPPING_KNOWLEDGE_TIME");
            var h = Horizons(P.RelativeStrengthHorizons,"RelativeStrengthHorizons");
            var stock = Bars(id,h.Max()+1); var benchmark = SelectBars(Data.Benchmark,h.Max()+1);
            return Weighted(h.Select(n => stock[^1].Close/stock[^(n+1)].Close-benchmark[^1].Close/benchmark[^(n+1)].Close).ToArray(),P.RelativeStrengthWeights);
        }
        public decimal Rank(Guid id, decimal value, Func<Guid,decimal> feature)
        {
            // No unapproved missing-member deletion, imputation, or dynamic denominator.
            var population = Data.UniverseMembers.Select(member => member == id ? value : feature(member)).ToArray();
            return CandidateMath.Percentile(value,population);
        }
        public decimal[] Risk(Guid id)
        {
            if (P.LiquidityPrice is null) throw Definition("LiquidityPrice:explicit Close proxy required");
            var vn = Period(P.VolatilityHorizon,"VolatilityHorizon"); var dn = Period(P.DrawdownHorizon,"DrawdownHorizon");
            var an = Period(P.AtrPeriod,"AtrPeriod"); var ln = Period(P.LiquidityHorizon,"LiquidityHorizon");
            if (P.Variance is null || !Enum.IsDefined(P.Variance.Value)) throw Definition("VarianceDefinition");
            if (P.Atr is null || !Enum.IsDefined(P.Atr.Value)) throw Definition("AtrDefinition");
            if (P.Variance == VarianceDefinition.Sample && vn < 2) throw Invalid("SAMPLE_VARIANCE_N");
            var needed = Math.Max(Math.Max(vn+1,dn+1),Math.Max(an+1,ln));
            var bars = P.Atr == AtrDefinition.WilderSeededMean ? AllBars(id,needed) : Bars(id,needed);
            var returns = bars.TakeLast(vn+1).Zip(bars.TakeLast(vn),(a,b) => b.Close/a.Close-1).ToArray();
            var mean = returns.Average();
            var volatility = CandidateMath.Sqrt(returns.Sum(r => (r-mean)*(r-mean))/(P.Variance == VarianceDefinition.Sample ? vn-1 : vn));
            var peak = bars.TakeLast(dn+1).Max(b => b.Close); var drawdown = (peak-bars[^1].Close)/peak;
            var tr = bars.Zip(bars.Skip(1),(a,b) => Math.Max(b.High-b.Low,Math.Max(Math.Abs(b.High-a.Close),Math.Abs(b.Low-a.Close)))).ToArray();
            decimal atr;
            if (P.Atr == AtrDefinition.SimpleMean) atr = tr.TakeLast(an).Average();
            else { atr = tr.Take(an).Average(); foreach (var v in tr.Skip(an)) atr = (atr*(an-1)+v)/an; }
            var liquidity = CandidateMath.Median(bars.TakeLast(ln).Select(b => b.Close*b.Volume));
            return [volatility,drawdown,atr/bars[^1].Close,liquidity];
        }
    }
}
