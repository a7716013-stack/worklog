using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.V2;
namespace WorkJournal.Web.Services.Evidence;

public interface IFullEvidencePreparationService
{
    Task<EvidencePreparation> PrepareAsync(FullEvidenceRequest request, CancellationToken ct = default);
}
// Read-only research boundary. Never chooses latest references or substitutes guessed normalizations.
// Intentionally does not persist a score/DecisionTrace until all six model definitions are specified.
public sealed class FullEvidencePreparationService(JournalDbContext db, IPitQueryService pit) : IFullEvidencePreparationService
{
    public async Task<EvidencePreparation> PrepareAsync(FullEvidenceRequest request, CancellationToken ct = default)
    {
        request = request with { DataCutoff = request.DataCutoff.ToUniversalTime() };
        var dataset = await db.Set<DatasetSnapshot>().AsNoTracking().SingleAsync(x => x.Id == request.DatasetSnapshotId, ct);
        var universe = await db.Set<UniverseSnapshot>().AsNoTracking().SingleAsync(x => x.Id == request.UniverseSnapshotId, ct);
        var versions = await db.Set<VersionSet>().AsNoTracking().SingleAsync(x => x.Id == request.VersionSetId, ct);
        var parameters = await db.Set<ParameterSet>().AsNoTracking().SingleAsync(x => x.Id == request.ParameterSetId, ct);
        if (new IFoundationDocument[] { dataset, universe, versions, parameters }.Any(x => x.Status != FoundationStatus.Finalized))
            throw new InvalidOperationException("Research requires finalized reference documents.");
        if (dataset.DataCutoff != request.DataCutoff || universe.DataCutoff != request.DataCutoff ||
            dataset.AsOfDate != request.TradeDate || universe.TradeDate != request.TradeDate ||
            versions.DataVersion != dataset.DatasetVersion || versions.UniverseVersion != universe.UniverseVersion)
            throw new InvalidOperationException("Research reference date/cutoff/version mismatch.");
        var members = await pit.GetUniverseAsync(request.UniverseSnapshotId, request.DataCutoff, ct);
        if (!members.Any(x => x.InstrumentId == request.InstrumentId && x.EligibilityStatus == UniverseEligibility.Eligible))
            throw new InvalidOperationException("Instrument is not eligible in the specified research universe. This is not a trade decision.");
        var benchmark = await db.Set<BenchmarkVersion>().AsNoTracking().SingleAsync(x => x.Id == versions.BenchmarkVersionId, ct);
        if (benchmark.ValidFrom > request.DataCutoff || (benchmark.ValidTo.HasValue && request.DataCutoff >= benchmark.ValidTo))
            throw new InvalidOperationException("Benchmark version is outside its effective interval.");
        if (!await db.Set<BenchmarkMapping>().AsNoTracking().AnyAsync(x => x.InstrumentId == request.InstrumentId &&
            x.BenchmarkVersionId == benchmark.Id && x.ValidFrom <= request.DataCutoff && (x.ValidTo == null || request.DataCutoff < x.ValidTo), ct))
            throw new InvalidOperationException("Specified historical benchmark mapping is missing.");
        var inputs = await pit.GetDataAsync(request.InstrumentId, request.DataCutoff, request.DatasetSnapshotId, ct);
        return new(request, benchmark.Id, versions.ModelVersion, versions.FeatureVersion, parameters.Checksum,
            Array.AsReadOnly(inputs.ToArray()), FullEvidenceModel.Gaps);
    }
}
