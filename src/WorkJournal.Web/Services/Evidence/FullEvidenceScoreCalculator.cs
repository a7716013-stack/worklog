using System.Collections.ObjectModel;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.V2;
namespace WorkJournal.Web.Services.Evidence;

public static class FullEvidenceModel
{
    // Locked outer formula only. This identifier does not claim that six feature definitions are frozen.
    public const string AggregationVersion = "FullEvidence100.Aggregation.v1";
    public static IReadOnlyDictionary<EvidenceCode, int> Weights { get; } = new ReadOnlyDictionary<EvidenceCode, int>(
        new Dictionary<EvidenceCode, int>
        {
            [EvidenceCode.Trend] = 25, [EvidenceCode.Momentum] = 15, [EvidenceCode.VolumePrice] = 15,
            [EvidenceCode.Institutional] = 20, [EvidenceCode.RelativeStrength] = 15, [EvidenceCode.RiskQuality] = 10
        });
    public static IReadOnlyList<ModelDefinitionGap> Gaps { get; } = Array.AsReadOnly(new[]
    {
        new ModelDefinitionGap(EvidenceCode.Trend, "TREND_DEFINITION_UNRESOLVED", "Alignment/position/slope/structure normalization, lookbacks and subweights."),
        new ModelDefinitionGap(EvidenceCode.Momentum, "MOMENTUM_DEFINITION_UNRESOLVED", "RSI curve, MACD scale denominator, ROC lookback and subweights."),
        new ModelDefinitionGap(EvidenceCode.VolumePrice, "VOLUMEPRICE_DEFINITION_UNRESOLVED", "RVOL lookback, price-volume confirmation, breakout normalization and subweights."),
        new ModelDefinitionGap(EvidenceCode.Institutional, "INSTITUTIONAL_DEFINITION_UNRESOLVED", "Flow denominator, horizons, investor weights and persistence normalization."),
        new ModelDefinitionGap(EvidenceCode.RelativeStrength, "RELATIVESTRENGTH_DEFINITION_UNRESOLVED", "Return horizons, percentile ties/missing/singleton rules and horizon weights."),
        new ModelDefinitionGap(EvidenceCode.RiskQuality, "RISKQUALITY_DEFINITION_UNRESOLVED", "Volatility/drawdown/ATR/liquidity definitions, horizons, curves and subweights.")
    });
}

public interface IFullEvidenceScoreCalculator
{
    EvidenceScore Calculate(IEnumerable<NormalizedEvidence> evidence);
}
public sealed class FullEvidenceScoreCalculator : IFullEvidenceScoreCalculator
{
    public EvidenceScore Calculate(IEnumerable<NormalizedEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var inputs = evidence.ToArray();
        if (inputs.Any(x => x is null || !Enum.IsDefined(x.Code)) || inputs.Select(x => x.Code).Distinct().Count() != inputs.Length)
            throw new ArgumentException("Unknown or duplicate evidence code.");
        if (inputs.Length != FullEvidenceModel.Weights.Count)
            throw new ArgumentException("All six evidence codes must be explicit; use a status for missing inputs.");
        var contributions = new List<EvidenceContribution>();
        var availableWeight = 0;
        decimal total = 0;
        foreach (var pair in FullEvidenceModel.Weights)
        {
            var input = inputs.Single(x => x.Code == pair.Key);
            if (!Enum.IsDefined(input.Status) || string.IsNullOrWhiteSpace(input.ReasonCode) ||
                string.IsNullOrWhiteSpace(input.FeatureVersion) || string.IsNullOrWhiteSpace(input.NormalizationVersion))
                throw new ArgumentException("Evidence status, reason and definition versions are required.");
            if (input.Value is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(evidence), "Normalized evidence must be in [0,1]; no silent clamp.");
            if (input.Status == EvidenceStatus.Available && input.Value is null)
                throw new ArgumentException("Available evidence requires a value.");
            if (input.Status != EvidenceStatus.Available && input.Value is not null)
                throw new ArgumentException("Unavailable normalized evidence cannot carry an authoritative score value; retain raw facts separately.");
            decimal? points = null;
            if (input.Status == EvidenceStatus.Available)
            {
                // Preserve decimal arithmetic. No intermediate or final quantization; display formatting is separate.
                points = input.Value!.Value * pair.Value;
                total += points.Value;
                availableWeight += pair.Value;
            }
            contributions.Add(new(input.Code, input.Value, pair.Value, points, input.Status,
                input.ReasonCode, input.FeatureVersion, input.NormalizationVersion));
        }
        var complete = availableWeight == 100;
        return new(complete ? EvidenceScoreStatus.Complete : EvidenceScoreStatus.Incomplete,
            complete ? total : null, availableWeight, FullEvidenceModel.AggregationVersion,
            contributions.AsReadOnly());
    }
}
