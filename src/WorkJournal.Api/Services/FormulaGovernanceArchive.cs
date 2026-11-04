using System.Text.Json;
using WorkJournal.Domain.Research;
namespace WorkJournal.Api.Services;
public sealed record FormulaGovernanceArchive(CandidateHypothesis[] Hypotheses,CandidateComparison[] Comparisons,CalibrationReceipt[] Calibrations,CandidateFreezeReceipt[] Freezes)
{
    // Same release-snapshot semantics as authority-state.json; not a mutable public approval store.
    public static FormulaGovernanceArchive Read()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"Research","formula-governance.json");
        if(!File.Exists(path))throw new ApiFault(503,"DATA_NOT_READY");
        return JsonSerializer.Deserialize<FormulaGovernanceArchive>(File.ReadAllText(path),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new ApiFault(503,"DATA_NOT_READY");
    }
}
