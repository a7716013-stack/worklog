using System.Collections.Immutable;
using WorkJournal.Web.Models.Evidence;
namespace WorkJournal.Web.Services.Evidence;

public static class CandidateFeatureCatalog
{
    // Reuses Phase 3 FeatureDefinition/NormalizationDefinition contracts; all definitions stay Candidate.
    public static ImmutableArray<FeatureDefinition> Create(Guid parameterSetId)
    {
        const string v = V3CandidateCalculator.Version;
        FeatureDefinition F(string code,EvidenceCode domain,string input,string window,NormalizationMethod method) =>
            new(code,v,domain,input,window,new(code+".Normalization",v,method,parameterSetId,DefinitionStatus.Candidate),DefinitionStatus.Candidate);
        return [
            F("Trend.Alignment",EvidenceCode.Trend,"Mean((MA20-MA60)/MA60,(MA60-MA120)/MA120)","MA periods",NormalizationMethod.LinearBounded),
            F("Trend.Position",EvidenceCode.Trend,".6*(Close-MA20)/MA20+.4*(Close-MA60)/MA60","MA periods",NormalizationMethod.LinearBounded),
            F("Trend.Slope",EvidenceCode.Trend,".6*(MA20[t]/MA20[t-N]-1)+.4*(MA60[t]/MA60[t-N]-1)","SlopeHorizon",NormalizationMethod.LinearBounded),
            F("Trend.Structure",EvidenceCode.Trend,"(Close-PriorLow)/(PriorHigh-PriorLow); Clamp01","StructureHorizon; excludes current bar",NormalizationMethod.LinearBounded),
            F("Momentum.RSI",EvidenceCode.Momentum,"Repository RSI14 Wilder primitive","Complete snapshot history, 15+ bars",NormalizationMethod.Piecewise),
            F("Momentum.MACD",EvidenceCode.Momentum,"Repository EMA12/26/9 Histogram / Close","Complete snapshot history, 34+ bars",NormalizationMethod.LinearBounded),
            F("Momentum.ROC",EvidenceCode.Momentum,"Close[t]/Close[t-N]-1","RocHorizon",NormalizationMethod.LinearBounded),
            F("VolumePrice.RVOLLog",EvidenceCode.VolumePrice,"ln(Volume/Mean(PriorVolume))","VolumeHorizon; excludes current bar",NormalizationMethod.LogBounded),
            F("VolumePrice.PVC",EvidenceCode.VolumePrice,"Return*ln(RVOL)","ReturnHorizon + VolumeHorizon",NormalizationMethod.LinearBounded),
            F("VolumePrice.Breakout",EvidenceCode.VolumePrice,"Close/PriorHigh-1","BreakoutHorizon; excludes current bar",NormalizationMethod.LinearBounded),
            F("Institutional.Foreign",EvidenceCode.Institutional,"Weighted Sum(ForeignShares)/Sum(VolumeShares)","InstitutionalHorizons includes current session",NormalizationMethod.Percentile),
            F("Institutional.Trust",EvidenceCode.Institutional,"Weighted Sum(TrustShares)/Sum(VolumeShares)","InstitutionalHorizons includes current session",NormalizationMethod.Percentile),
            F("Institutional.Dealer",EvidenceCode.Institutional,"Weighted Sum(DealerShares)/Sum(VolumeShares)","InstitutionalHorizons includes current session",NormalizationMethod.Percentile),
            F("Institutional.Persistence",EvidenceCode.Institutional,"PositiveDays/ValidDays; explicitly selected group; identity [0,1]","PersistenceHorizon; no missing sessions allowed",NormalizationMethod.LinearBounded),
            F("RelativeStrength.ExcessReturn",EvidenceCode.RelativeStrength,"Weighted(stock return - historical benchmark return)","RelativeStrengthHorizons",NormalizationMethod.Percentile),
            F("Risk.Volatility",EvidenceCode.RiskQuality,"StdDev(simple daily returns); explicit sample/population policy","VolatilityHorizon returns",NormalizationMethod.InversePercentile),
            F("Risk.Drawdown",EvidenceCode.RiskQuality,"(Peak-Close)/Peak","DrawdownHorizon+1 close observations, includes current",NormalizationMethod.InversePercentile),
            F("Risk.ATR",EvidenceCode.RiskQuality,"Explicit SimpleMean or WilderSeededMean ATR / Close","AtrPeriod; seed uses complete snapshot history for Wilder",NormalizationMethod.InversePercentile),
            F("Risk.Liquidity",EvidenceCode.RiskQuality,"Median(Close*VolumeShares)","LiquidityHorizon, includes current",NormalizationMethod.Percentile)
        ];
    }
}
