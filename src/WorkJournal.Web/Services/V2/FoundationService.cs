using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models.V2;
namespace WorkJournal.Web.Services.V2;

public interface IInstrumentIdentityService
{
    Task<Instrument> RegisterAsync(string permanentKey, string name, string type, string currency, DateTimeOffset validFrom, CancellationToken ct = default);
    Task AssignSymbolAsync(Guid instrumentId, string market, string symbol, DateTimeOffset from, DateTimeOffset? to, CancellationToken ct = default);
    Task MapLegacyAsync(Guid instrumentId, string source, string market, string stockId, DateTimeOffset from, DateTimeOffset? to, CancellationToken ct = default);
    Task<InstrumentSymbolHistory?> SymbolAtAsync(Guid instrumentId, DateTimeOffset at, CancellationToken ct = default);
    Task<Guid?> ResolveLegacyAsync(string source, string market, string stockId, DateTimeOffset at, CancellationToken ct = default);
}
public interface IDatasetSnapshotService { Task<DatasetSnapshot> CreateDatasetAsync(DatasetSnapshot input, CancellationToken ct = default); }
public interface IUniverseSnapshotService { Task<UniverseSnapshot> CreateUniverseAsync(UniverseSnapshot input, CancellationToken ct = default); }
public interface IVersionSetService { Task<VersionSet> CreateVersionSetAsync(VersionSet input, CancellationToken ct = default); }
public interface IParameterSetService { Task<ParameterSet> CreateParametersAsync(string code, string version, string json, CancellationToken ct = default); }
public interface IDecisionTraceService { Task<DecisionTrace> RecordDecisionAsync(DecisionTrace input, CancellationToken ct = default); }
public interface IPitQueryService
{
    Task<IReadOnlyList<DataProvenance>> GetDataAsync(Guid instrumentId, DateTimeOffset dataCutoff, Guid datasetSnapshotId, CancellationToken ct = default);
    Task<IReadOnlyList<UniverseSnapshotMember>> GetUniverseAsync(Guid universeSnapshotId, DateTimeOffset dataCutoff, CancellationToken ct = default);
    Task<DecisionTrace?> GetDecisionAsync(Guid decisionId, CancellationToken ct = default);
}

// Single modular SQL boundary. No HTTP/provider dependencies and no scoring or trading policies.
public sealed class FoundationService(JournalDbContext db, TimeProvider time) : IInstrumentIdentityService,
    IDatasetSnapshotService, IUniverseSnapshotService, IVersionSetService, IParameterSetService, IDecisionTraceService, IPitQueryService
{
    private static void Required(string value, string name) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(name + " is required."); }
    private static void Period(DateTimeOffset from, DateTimeOffset? to) { if (to <= from) throw new ArgumentException("Invalid half-open period."); }
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private async Task<T> Write<T>(string resource, Func<Task<T>> action, CancellationToken ct)
    {
        if (db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Foundation writes require a clean unit of work.");
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                // Transaction-owned lock works across processes; identities still have unique constraints.
                await db.Database.ExecuteSqlInterpolatedAsync($@"DECLARE @r int;
EXEC @r = sys.sp_getapplock @Resource={"WorkJournal:V2:" + resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
IF @r < 0 THROW 51000, 'V2 coordination lock unavailable', 1;", ct);
                var result = await action();
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return result;
            }
            catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
        });
    }
    private async Task<T> Store<T>(T doc, string semantic, CancellationToken ct) where T : class, IFoundationDocument
    {
        doc.Id = CanonicalJson.Identity(typeof(T).Name, semantic);
        doc.Checksum = CanonicalJson.Hash(semantic);
        var prior = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == doc.Id, ct);
        if (prior is not null)
        {
            if (prior.Checksum != doc.Checksum || prior.Status != FoundationStatus.Finalized) throw new InvalidOperationException("Identity conflict.");
            return prior;
        }
        doc.Status = FoundationStatus.Finalized;
        doc.CreatedAt = time.GetUtcNow();
        db.Add(doc);
        return doc;
    }
    public Task<Instrument> RegisterAsync(string permanentKey, string name, string type, string currency, DateTimeOffset validFrom, CancellationToken ct = default)
    {
        Required(permanentKey, nameof(permanentKey)); Required(name, nameof(name)); Required(type, nameof(type));
        if (currency.Length != 3 || currency.Any(c => c < 'A' || c > 'Z')) throw new ArgumentException("Currency must be an explicit three-letter uppercase code.");
        return Write("Identity", async () =>
        {
            var prior = await db.Set<Instrument>().AsNoTracking().SingleOrDefaultAsync(x => x.PermanentKey == permanentKey, ct);
            if (prior is not null)
            {
                if (prior.InstrumentType != type || prior.Currency != currency || prior.ValidFrom != validFrom) throw new InvalidOperationException("Permanent identity metadata conflict.");
                return prior;
            }
            var item = new Instrument { InstrumentId = CanonicalJson.Identity("Instrument", permanentKey), PermanentKey = permanentKey, Name = name, InstrumentType = type, Currency = currency, ValidFrom = validFrom.ToUniversalTime(), IsActive = true };
            db.Add(item); return item;
        }, ct);
    }
    public Task AssignSymbolAsync(Guid instrumentId, string market, string symbol, DateTimeOffset from, DateTimeOffset? to, CancellationToken ct = default)
    {
        Required(market, nameof(market)); Required(symbol, nameof(symbol)); Period(from, to);
        from = from.ToUniversalTime(); to = to?.ToUniversalTime();
        return Write("Identity", async () =>
        {
            var instrument = await db.Set<Instrument>().SingleAsync(x => x.InstrumentId == instrumentId, ct);
            if (from < instrument.ValidFrom || (instrument.ValidTo.HasValue && (!to.HasValue || to > instrument.ValidTo))) throw new ArgumentException("Symbol period outside instrument lifetime.");
            var rows = await db.Set<InstrumentSymbolHistory>().Where(x => x.InstrumentId == instrumentId || (x.Market == market && x.Symbol == symbol)).ToListAsync(ct);
            if (rows.Any(x => x.InstrumentId == instrumentId && x.Market == market && x.Symbol == symbol && x.ValidFrom == from && x.ValidTo == to)) return true;
            foreach (var row in rows.Where(x => (!x.ValidTo.HasValue || x.ValidTo > from) && (!to.HasValue || x.ValidFrom < to)))
            {
                // A later symbol assignment closes the previous open display interval.
                if (row.InstrumentId == instrumentId && row.ValidTo is null && row.ValidFrom < from) row.ValidTo = from;
                else throw new InvalidOperationException("Overlapping symbol identity interval.");
            }
            db.Add(new InstrumentSymbolHistory { Id = CanonicalJson.Identity("Symbol", CanonicalJson.Of(new { instrumentId, market, symbol, from })), InstrumentId = instrumentId, Market = market, Symbol = symbol, ValidFrom = from, ValidTo = to });
            return true;
        }, ct);
    }
    public Task MapLegacyAsync(Guid instrumentId, string source, string market, string stockId, DateTimeOffset from, DateTimeOffset? to, CancellationToken ct = default)
    {
        Required(source, nameof(source)); Required(market, nameof(market)); Required(stockId, nameof(stockId)); Period(from, to);
        from = from.ToUniversalTime(); to = to?.ToUniversalTime();
        return Write("Identity", async () =>
        {
            var rows = await db.Set<LegacyInstrumentMapping>().Where(x => x.Source == source && x.Market == market && x.StockId == stockId).ToListAsync(ct);
            if (rows.Any(x => x.InstrumentId == instrumentId && x.ValidFrom == from && x.ValidTo == to)) return true;
            if (rows.Any(x => (!x.ValidTo.HasValue || x.ValidTo > from) && (!to.HasValue || x.ValidFrom < to))) throw new InvalidOperationException("Ambiguous legacy identity mapping.");
            var instrument = await db.Set<Instrument>().SingleAsync(x => x.InstrumentId == instrumentId, ct);
            if (from < instrument.ValidFrom || (instrument.ValidTo.HasValue && (!to.HasValue || to > instrument.ValidTo))) throw new ArgumentException("Mapping period outside instrument lifetime.");
            db.Add(new LegacyInstrumentMapping { Id = CanonicalJson.Identity("LegacyMapping", CanonicalJson.Of(new { instrumentId, source, market, stockId, from })), InstrumentId = instrumentId, Source = source, Market = market, StockId = stockId, ValidFrom = from, ValidTo = to });
            return true;
        }, ct);
    }
    public Task<InstrumentSymbolHistory?> SymbolAtAsync(Guid instrumentId, DateTimeOffset at, CancellationToken ct = default) =>
        db.Set<InstrumentSymbolHistory>().AsNoTracking().SingleOrDefaultAsync(x => x.InstrumentId == instrumentId && x.ValidFrom <= at && (x.ValidTo == null || at < x.ValidTo), ct);
    public Task<Guid?> ResolveLegacyAsync(string source, string market, string stockId, DateTimeOffset at, CancellationToken ct = default) =>
        db.Set<LegacyInstrumentMapping>().AsNoTracking().Where(x => x.Source == source && x.Market == market && x.StockId == stockId && x.ValidFrom <= at && (x.ValidTo == null || at < x.ValidTo)).Select(x => (Guid?)x.InstrumentId).SingleOrDefaultAsync(ct);

    public Task<DatasetSnapshot> CreateDatasetAsync(DatasetSnapshot input, CancellationToken ct = default)
    {
        var d = Copy(input); Required(d.DatasetVersion, nameof(d.DatasetVersion)); Required(d.SourceSummary, nameof(d.SourceSummary));
        d.DataCutoff = d.DataCutoff.ToUniversalTime();
        foreach (var p in d.Records)
        {
            Required(p.Provider, nameof(p.Provider)); Required(p.Dataset, nameof(p.Dataset)); Required(p.SourceKey, nameof(p.SourceKey));
            if (!Enum.IsDefined(p.QualityStatus)) throw new ArgumentException("Unknown quality status.");
            p.EffectiveAt = p.EffectiveAt.ToUniversalTime(); p.AvailableAt = p.AvailableAt?.ToUniversalTime(); p.ObservedAt = p.ObservedAt.ToUniversalTime(); p.RetrievedAt = p.RetrievedAt.ToUniversalTime();
            if (p.RetrievedAt < p.ObservedAt) throw new ArgumentException("Retrieval cannot precede first observation.");
            p.PayloadJson = CanonicalJson.Normalize(p.PayloadJson); p.Checksum = CanonicalJson.Hash(p.PayloadJson);
            p.Id = Guid.Empty; p.DatasetSnapshotId = Guid.Empty;
        }
        var records = d.Records.Select(p => CanonicalJson.Of(p)).Order(StringComparer.Ordinal).ToArray();
        if (records.Distinct(StringComparer.Ordinal).Count() != records.Length) throw new ArgumentException("Duplicate snapshot record.");
        var semantic = CanonicalJson.Of(new { d.AsOfDate, d.DataCutoff, d.DatasetVersion, d.SourceSummary, records });
        return Write("Dataset", async () =>
        {
            d.Id = CanonicalJson.Identity(nameof(DatasetSnapshot), semantic);
            foreach (var p in d.Records) { p.Id = CanonicalJson.Identity("Provenance", d.Id + CanonicalJson.Of(p)); p.DatasetSnapshotId = d.Id; }
            var result = await Store(d, semantic, ct);
            if (!ReferenceEquals(result, d)) result.Records = await db.Set<DataProvenance>().AsNoTracking().Where(x => x.DatasetSnapshotId == result.Id).ToListAsync(ct);
            return result;
        }, ct);
    }
    public Task<UniverseSnapshot> CreateUniverseAsync(UniverseSnapshot input, CancellationToken ct = default)
    {
        var u = Copy(input); Required(u.UniverseVersion, nameof(u.UniverseVersion)); u.DataCutoff = u.DataCutoff.ToUniversalTime();
        if (u.Members.Select(x => x.InstrumentId).Distinct().Count() != u.Members.Count) throw new ArgumentException("Duplicate universe instrument.");
        foreach (var m in u.Members) { if (!Enum.IsDefined(m.EligibilityStatus)) throw new ArgumentException("Unknown universe eligibility."); Required(m.ReasonCode, nameof(m.ReasonCode)); m.UniverseSnapshotId = Guid.Empty; }
        var semantic = CanonicalJson.Of(new { u.TradeDate, u.DataCutoff, u.UniverseVersion, members = u.Members.OrderBy(x => x.InstrumentId).ToArray() });
        return Write("Universe", async () =>
        {
            // Assign the stable parent key before EF tracks child composite keys.
            u.Id = CanonicalJson.Identity(nameof(UniverseSnapshot), semantic);
            foreach (var member in u.Members) member.UniverseSnapshotId = u.Id;
            var result = await Store(u, semantic, ct);
            if (!ReferenceEquals(result, u)) result.Members = await db.Set<UniverseSnapshotMember>().AsNoTracking().Where(x => x.UniverseSnapshotId == result.Id).ToListAsync(ct);
            return result;
        }, ct);
    }
    public Task<VersionSet> CreateVersionSetAsync(VersionSet input, CancellationToken ct = default)
    {
        var v = Copy(input);
        Required(v.ModelVersion, nameof(v.ModelVersion)); Required(v.FeatureVersion, nameof(v.FeatureVersion)); Required(v.UniverseVersion, nameof(v.UniverseVersion)); Required(v.DataVersion, nameof(v.DataVersion));
        v.ExtensionsJson = CanonicalJson.Normalize(v.ExtensionsJson);
        var semantic = CanonicalJson.Of(new { v.ModelVersion, v.FeatureVersion, v.UniverseVersion, v.BenchmarkVersionId, v.DataVersion, v.StrategyVersion, v.ExtensionsJson });
        return Write("Version", () => Store(v, semantic, ct), ct);
    }
    public Task<ParameterSet> CreateParametersAsync(string code, string version, string json, CancellationToken ct = default)
    {
        Required(code, nameof(code)); Required(version, nameof(version));
        var canonical = CanonicalJson.Normalize(json); var checksum = CanonicalJson.Hash(canonical);
        return Write("Parameter", async () =>
        {
            var prior = await db.Set<ParameterSet>().AsNoTracking().SingleOrDefaultAsync(x => x.Code == code && x.Version == version, ct);
            if (prior is not null)
            {
                if (prior.ParametersJson != canonical || prior.Status != FoundationStatus.Finalized) throw new InvalidOperationException("Parameter version conflict; create a new version.");
                return prior;
            }
            var p = new ParameterSet { Id = CanonicalJson.Identity("Parameter", CanonicalJson.Of(new { code, version, canonical })), Code = code, Version = version, ParametersJson = canonical, Checksum = checksum, Status = FoundationStatus.Finalized, CreatedAt = time.GetUtcNow() };
            db.Add(p); return p;
        }, ct);
    }
    public Task<DecisionTrace> RecordDecisionAsync(DecisionTrace input, CancellationToken ct = default)
    {
        var t = Copy(input); Required(t.DecisionStatus, nameof(t.DecisionStatus)); Required(t.ReasonCode, nameof(t.ReasonCode));
        if (!Enum.IsDefined(t.DecisionType)) throw new ArgumentException("Unknown decision type.");
        t.DataCutoff = t.DataCutoff.ToUniversalTime(); t.DecisionTime = t.DecisionTime.ToUniversalTime(); t.DetailsJson = CanonicalJson.Normalize(t.DetailsJson);
        if (t.DataCutoff > t.DecisionTime) throw new ArgumentException("Decision cannot use a future cutoff.");
        return Write("Decision", async () =>
        {
            var d = await db.Set<DatasetSnapshot>().AsNoTracking().SingleAsync(x => x.Id == t.DatasetSnapshotId, ct);
            var u = await db.Set<UniverseSnapshot>().AsNoTracking().SingleAsync(x => x.Id == t.UniverseSnapshotId, ct);
            var v = await db.Set<VersionSet>().AsNoTracking().SingleAsync(x => x.Id == t.VersionSetId, ct);
            var p = await db.Set<ParameterSet>().AsNoTracking().SingleAsync(x => x.Id == t.ParameterSetId, ct);
            if (new IFoundationDocument[] { d, u, v, p }.Any(x => x.Status != FoundationStatus.Finalized)) throw new InvalidOperationException("Trace requires finalized references.");
            if (d.DataCutoff != t.DataCutoff || u.DataCutoff != t.DataCutoff || u.TradeDate != t.TradeDate || d.AsOfDate != t.TradeDate || d.DatasetVersion != v.DataVersion || u.UniverseVersion != v.UniverseVersion)
                throw new InvalidOperationException("Trace snapshot cutoff/date/version mismatch.");
            if (!await db.Set<UniverseSnapshotMember>().AnyAsync(x => x.UniverseSnapshotId == u.Id && x.InstrumentId == t.InstrumentId, ct)) throw new InvalidOperationException("Instrument not represented in historical universe.");
            if (!await db.Set<BenchmarkMapping>().AnyAsync(x => x.InstrumentId == t.InstrumentId && x.BenchmarkVersionId == v.BenchmarkVersionId && x.ValidFrom <= t.DataCutoff && (x.ValidTo == null || t.DataCutoff < x.ValidTo), ct)) throw new InvalidOperationException("Historical benchmark mapping missing.");
            var benchmark = await db.Set<BenchmarkVersion>().AsNoTracking().SingleAsync(x => x.Id == v.BenchmarkVersionId, ct);
            if (benchmark.ValidFrom > t.DataCutoff || (benchmark.ValidTo.HasValue && t.DataCutoff >= benchmark.ValidTo)) throw new InvalidOperationException("Benchmark version outside effective interval.");
            var semantic = CanonicalJson.Of(new { t.InstrumentId, t.TradeDate, t.DecisionTime, t.DataCutoff, t.DatasetSnapshotId, t.UniverseSnapshotId, t.VersionSetId, t.ParameterSetId, t.DecisionType, t.DecisionStatus, t.ReasonCode, t.DetailsJson });
            return await Store(t, semantic, ct);
        }, ct);
    }
    public async Task<IReadOnlyList<DataProvenance>> GetDataAsync(Guid instrumentId, DateTimeOffset dataCutoff, Guid datasetSnapshotId, CancellationToken ct = default)
    {
        var snapshot = await db.Set<DatasetSnapshot>().AsNoTracking().SingleAsync(x => x.Id == datasetSnapshotId, ct);
        if (snapshot.Status != FoundationStatus.Finalized || snapshot.DataCutoff != dataCutoff) throw new InvalidOperationException("Replay requires the exact finalized snapshot cutoff.");
        return await db.Set<DataProvenance>().AsNoTracking().Where(x => x.DatasetSnapshotId == datasetSnapshotId && x.InstrumentId == instrumentId && x.AvailableAt != null && x.AvailableAt <= dataCutoff).OrderBy(x => x.EffectiveAt).ThenBy(x => x.Id).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<UniverseSnapshotMember>> GetUniverseAsync(Guid universeSnapshotId, DateTimeOffset dataCutoff, CancellationToken ct = default)
    {
        var u = await db.Set<UniverseSnapshot>().AsNoTracking().SingleAsync(x => x.Id == universeSnapshotId, ct);
        if (u.Status != FoundationStatus.Finalized || u.DataCutoff != dataCutoff) throw new InvalidOperationException("Replay requires the exact historical universe cutoff.");
        return await db.Set<UniverseSnapshotMember>().AsNoTracking().Where(x => x.UniverseSnapshotId == universeSnapshotId).OrderBy(x => x.InstrumentId).ToListAsync(ct);
    }
    public Task<DecisionTrace?> GetDecisionAsync(Guid decisionId, CancellationToken ct = default) => db.Set<DecisionTrace>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == decisionId, ct);
}
