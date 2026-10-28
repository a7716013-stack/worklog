using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.V2;

var passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS: " + label); passed++; }
async Task Reject(Func<Task> action, string label)
{
    try { await action(); } catch (Exception e) when (e is InvalidOperationException or ArgumentException or DbUpdateException) { Check(true, label); return; }
    throw new Exception("Expected rejection: " + label);
}
var left = CanonicalJson.Normalize("{\"b\":[2,3],\"a\":1.00}");
Check(left == CanonicalJson.Normalize("{\"a\":1e0,\"b\":[2,3]}"), "canonical property order and exact numeric equivalence");
Check(CanonicalJson.Hash(left) != CanonicalJson.Hash(CanonicalJson.Normalize("{\"a\":2,\"b\":[2,3]}")), "changed parameter changes checksum");
Check(CanonicalJson.Normalize("[1,2]") != CanonicalJson.Normalize("[2,1]"), "array ordering remains semantic");
Check(CanonicalJson.Normalize("9007199254740993") != CanonicalJson.Normalize("9007199254740992"), "large integer precision retained");
await Reject(() => Task.FromResult(CanonicalJson.Normalize("{\"a\":1,\"a\":2}")), "duplicate JSON keys rejected");
Check(new EvidenceValue(0, EvidenceStatus.Available, "zero observed", null, null, "fixture") != new EvidenceValue(null, EvidenceStatus.Missing, "unknown", null, null, "fixture"), "missing is not zero");
Check(Enum.GetValues<EvidenceStatus>().Length == 5, "five distinct evidence quality statuses");
if (args.Contains("--fast")) { Console.WriteLine($"V2 fast checks: {passed} passed"); return; }

TestDatabase.CheckSafety();
const string prefix = "WorkJournalPaperChecks_"; // Reuse the existing guarded ephemeral database owner.
var database = prefix + Guid.NewGuid().ToString("N");
var options = new DbContextOptionsBuilder<JournalDbContext>().UseSqlServer(TestDatabase.Connection(prefix, database), sql => sql.EnableRetryOnFailure()).Options;
using var db = new JournalDbContext(options);
var clock = new FixedClock();
var cutoff = new DateTimeOffset(2026, 1, 5, 13, 30, 0, TimeSpan.FromHours(8));
var date = new DateOnly(2026, 1, 5);
var start = cutoff.AddYears(-1);
var service = new FoundationService(db, clock);
try
{
    await db.Database.MigrateAsync();
    await TestDatabase.PrintVersionAsync(db);
    Check(!db.Database.HasPendingModelChanges(), "EF model matches migrations");
    var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    Check(migrations.Any(x => x.EndsWith("AddV2InstrumentIdentity")) && migrations.Any(x => x.EndsWith("AddV2SnapshotsAndDecisionFoundations")), "both additive V2 migrations applied");
    var first = await service.RegisterAsync("fixture-security-1", "Fixture", "Equity", "TWD", start);
    var again = await service.RegisterAsync("fixture-security-1", "Fixture", "Equity", "TWD", start);
    Check(first.InstrumentId == again.InstrumentId, "permanent identity deterministic on retry");
    await service.AssignSymbolAsync(first.InstrumentId, "TEST", "OLD", start, null);
    await service.AssignSymbolAsync(first.InstrumentId, "TEST", "NEW", cutoff, null);
    Check((await service.SymbolAtAsync(first.InstrumentId, cutoff.AddSeconds(-1)))?.Symbol == "OLD", "historical symbol before rename");
    Check((await service.SymbolAtAsync(first.InstrumentId, cutoff))?.Symbol == "NEW", "same instrument survives rename at exact boundary");
    var second = await service.RegisterAsync("fixture-security-2", "Second", "Equity", "USD", cutoff);
    await service.AssignSymbolAsync(second.InstrumentId, "TEST", "OLD", cutoff, null);
    Check(first.InstrumentId != second.InstrumentId, "reused symbol does not reuse permanent identity");
    await Reject(() => service.AssignSymbolAsync(second.InstrumentId, "TEST", "NEW", cutoff, null), "overlapping symbol ownership rejected");
    await service.MapLegacyAsync(first.InstrumentId, "Legacy", "TEST", "OLD", start, cutoff);
    await service.MapLegacyAsync(second.InstrumentId, "Legacy", "TEST", "OLD", cutoff, null);
    Check(await service.ResolveLegacyAsync("Legacy", "TEST", "OLD", cutoff.AddSeconds(-1)) == first.InstrumentId && await service.ResolveLegacyAsync("Legacy", "TEST", "OLD", cutoff) == second.InstrumentId, "legacy mapping is temporal and deterministic");
    db.ChangeTracker.Clear();
    db.Add(new Instrument { InstrumentId = Guid.NewGuid(), PermanentKey = first.PermanentKey, Name = "duplicate", InstrumentType = "Equity", Currency = "TWD", ValidFrom = start });
    await Reject(() => db.SaveChangesAsync(), "DB rejects duplicate permanent identity"); db.ChangeTracker.Clear();

    DatasetSnapshot Dataset() => new() { AsOfDate = date, DataCutoff = cutoff, DatasetVersion = "fixture-data-v1", SourceSummary = "deterministic fixture", Records = new[] { -1, 0, 1, 2 }.Select((offset, index) => new DataProvenance {
        InstrumentId = first.InstrumentId, Provider = "fixture", Dataset = "report", SourceKey = "row" + index,
        EffectiveAt = cutoff.AddMonths(-1), AvailableAt = index == 3 ? null : cutoff.AddSeconds(offset), ObservedAt = cutoff.AddDays(1), RetrievedAt = cutoff.AddDays(1),
        QualityStatus = index == 3 ? EvidenceStatus.Missing : EvidenceStatus.Available, PayloadJson = "{\"value\":" + index + "}"
    }).ToList() };
    var snapshot = await service.CreateDatasetAsync(Dataset());
    var retryInput = Dataset(); retryInput.Records.Reverse();
    var snapshotRetry = await service.CreateDatasetAsync(retryInput);
    Check(snapshot.Id == snapshotRetry.Id && snapshot.Checksum == snapshotRetry.Checksum && snapshotRetry.Records.Count == 4, "snapshot retry and record ordering preserve identity/content");
    var visible = await service.GetDataAsync(first.InstrumentId, cutoff, snapshot.Id);
    Check(visible.Count == 2 && visible.Any(x => x.AvailableAt == cutoff) && visible.Any(x => x.AvailableAt == cutoff.AddSeconds(-1)), "PIT boundary includes -1 second and exact cutoff");
    Check(visible.All(x => x.SourceKey != "row2" && x.SourceKey != "row3"), "PIT excludes future publication and unknown availability despite old event time");
    Check(visible.All(x => x.AvailableAt!.Value.Offset == TimeSpan.Zero && x.ObservedAt.Offset == TimeSpan.Zero), "persisted timestamps normalize to UTC");
    await Reject(() => service.GetDataAsync(first.InstrumentId, cutoff.AddSeconds(1), snapshot.Id), "replay cannot substitute another cutoff");
    db.ChangeTracker.Clear(); var mutable = await db.Set<DatasetSnapshot>().SingleAsync(x => x.Id == snapshot.Id); mutable.SourceSummary = "tampered";
    await Reject(() => db.SaveChangesAsync(), "finalized dataset header immutable"); db.ChangeTracker.Clear();
    var record = await db.Set<DataProvenance>().FirstAsync(x => x.DatasetSnapshotId == snapshot.Id); record.PayloadJson = "{}";
    await Reject(() => db.SaveChangesAsync(), "finalized dataset child immutable"); db.ChangeTracker.Clear();
    var extra = Dataset().Records[0]; extra.Id = Guid.NewGuid(); extra.DatasetSnapshotId = snapshot.Id; db.Add(extra);
    await Reject(() => db.SaveChangesAsync(), "cannot append to finalized snapshot"); db.ChangeTracker.Clear();

    UniverseSnapshot Universe() => new() { TradeDate = date, DataCutoff = cutoff, UniverseVersion = "fixture-universe-v1", Members = [new() { InstrumentId = first.InstrumentId, EligibilityStatus = UniverseEligibility.Eligible, ReasonCode = "fixture-member" }] };
    var universe = await service.CreateUniverseAsync(Universe());
    Check((await service.CreateUniverseAsync(Universe())).Id == universe.Id, "universe creation idempotent");
    var later = Universe(); later.DataCutoff = cutoff.AddDays(1); later.TradeDate = date.AddDays(1); later.Members[0].InstrumentId = second.InstrumentId;
    await service.CreateUniverseAsync(later);
    Check((await service.GetUniverseAsync(universe.Id, cutoff)).Single().InstrumentId == first.InstrumentId, "historical universe independent of newer membership");
    db.ChangeTracker.Clear(); var member = await db.Set<UniverseSnapshotMember>().SingleAsync(x => x.UniverseSnapshotId == universe.Id); member.EligibilityStatus = UniverseEligibility.Ineligible;
    await Reject(() => db.SaveChangesAsync(), "finalized universe member immutable"); db.ChangeTracker.Clear();

    var benchmark = new BenchmarkDefinition { Id = Guid.NewGuid(), Code = "fixture-benchmark", Name = "Fixture" };
    var bv = new BenchmarkVersion { Id = Guid.NewGuid(), BenchmarkDefinitionId = benchmark.Id, Version = "fixture-v1", ValidFrom = start };
    db.AddRange(benchmark, bv, new BenchmarkMapping { Id = Guid.NewGuid(), InstrumentId = first.InstrumentId, BenchmarkVersionId = bv.Id, ValidFrom = start }); await db.SaveChangesAsync();
    VersionSet Versions() => new() { ModelVersion = "contract-only-fixture", FeatureVersion = "fixture-features-v1", UniverseVersion = "fixture-universe-v1", DataVersion = "fixture-data-v1", BenchmarkVersionId = bv.Id };
    var versions = await service.CreateVersionSetAsync(Versions());
    Check((await service.CreateVersionSetAsync(Versions())).Id == versions.Id, "VersionSet stable identity");
    var changed = Versions(); changed.FeatureVersion = "fixture-features-v2";
    Check((await service.CreateVersionSetAsync(changed)).Id != versions.Id, "feature version changes VersionSet identity");
    db.ChangeTracker.Clear(); var frozen = await db.Set<VersionSet>().SingleAsync(x => x.Id == versions.Id); frozen.Status = FoundationStatus.Draft;
    await Reject(() => db.SaveChangesAsync(), "finalized VersionSet cannot downgrade to draft"); db.ChangeTracker.Clear();
    var parameters = await service.CreateParametersAsync("fixture", "v1", "{\"b\":2,\"a\":1}");
    Check((await service.CreateParametersAsync("fixture", "v1", "{\"a\":1.0,\"b\":2}")).Id == parameters.Id, "parameter canonical retry");
    await Reject(() => service.CreateParametersAsync("fixture", "v1", "{\"a\":3}"), "same named parameter version cannot change content");
    db.ChangeTracker.Clear(); var frozenParam = await db.Set<ParameterSet>().SingleAsync(x => x.Id == parameters.Id); db.Remove(frozenParam);
    await Reject(() => db.SaveChangesAsync(), "finalized parameter deletion rejected"); db.ChangeTracker.Clear();
    DecisionTrace Trace() => new() { InstrumentId = first.InstrumentId, TradeDate = date, DecisionTime = cutoff, DataCutoff = cutoff, DatasetSnapshotId = snapshot.Id, UniverseSnapshotId = universe.Id, VersionSetId = versions.Id, ParameterSetId = parameters.Id, DecisionType = DecisionKind.Research, DecisionStatus = "fixture-only", ReasonCode = "fixture-replay" };
    var trace = await service.RecordDecisionAsync(Trace());
    Check((await service.RecordDecisionAsync(Trace())).Id == trace.Id, "trace retry deterministic");
    var stored = await service.GetDecisionAsync(trace.Id);
    Check(stored is not null && stored.InstrumentId == first.InstrumentId && stored.DatasetSnapshotId == snapshot.Id && stored.UniverseSnapshotId == universe.Id && stored.VersionSetId == versions.Id && stored.ParameterSetId == parameters.Id, "durable trace exact reference linkage");
    var wrong = Trace(); wrong.DataCutoff = cutoff.AddSeconds(-1);
    await Reject(() => service.RecordDecisionAsync(wrong), "trace rejects mismatched snapshot cutoff");
    wrong = Trace(); wrong.InstrumentId = second.InstrumentId;
    await Reject(() => service.RecordDecisionAsync(wrong), "trace rejects instrument absent from historical universe");
    db.ChangeTracker.Clear(); var frozenTrace = await db.Set<DecisionTrace>().SingleAsync(x => x.Id == trace.Id); frozenTrace.ReasonCode = "tampered";
    await Reject(() => db.SaveChangesAsync(), "finalized decision trace immutable"); db.ChangeTracker.Clear();
    db.ChangeTracker.Clear();
    var detached = new VersionSet { Id = versions.Id, Status = FoundationStatus.Draft, ModelVersion = "tampered" };
    db.Update(detached);
    await Reject(() => db.SaveChangesAsync(), "detached entity cannot forge draft status to bypass immutability"); db.ChangeTracker.Clear();
    var badVersion = Versions(); badVersion.BenchmarkVersionId = Guid.NewGuid();
    await Reject(() => service.CreateVersionSetAsync(badVersion), "DB rejects missing benchmark foreign key");
    db.ChangeTracker.Clear();
    var badDataset = Dataset(); badDataset.Records[0].InstrumentId = Guid.NewGuid();
    await Reject(() => service.CreateDatasetAsync(badDataset), "DB rejects missing provenance instrument foreign key");
    db.ChangeTracker.Clear();
    var unknown = Trace(); unknown.ParameterSetId = Guid.NewGuid();
    await Reject(() => service.RecordDecisionAsync(unknown), "trace rejects unknown parameter reference");
    var badUniverseVersion = Versions(); badUniverseVersion.UniverseVersion = "different-universe";
    var otherVersions = await service.CreateVersionSetAsync(badUniverseVersion);
    var mismatchedTrace = Trace(); mismatchedTrace.VersionSetId = otherVersions.Id;
    await Reject(() => service.RecordDecisionAsync(mismatchedTrace), "trace rejects snapshot version mismatch");
    db.ChangeTracker.Clear(); var frozenBenchmark = await db.Set<BenchmarkVersion>().SingleAsync(x => x.Id == bv.Id); frozenBenchmark.DefinitionJson = "{\"modified\":true}";
    await Reject(() => db.SaveChangesAsync(), "historical benchmark definition immutable"); db.ChangeTracker.Clear();
    // Separate connections exercise the transaction-owned SQL lock, not an in-process semaphore.
    var concurrentInput = Dataset(); concurrentInput.SourceSummary = "new concurrent snapshot";
    var concurrent = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => { await using var other = new JournalDbContext(options); return await new FoundationService(other, clock).CreateDatasetAsync(concurrentInput); }));
    Check(concurrent.Select(x => x.Id).Distinct().Count() == 1 && concurrent[0].Id != snapshot.Id && await db.Set<DatasetSnapshot>().CountAsync(x => x.Id == concurrent[0].Id) == 1, "cross-connection concurrent snapshot retries create one row");
    Console.WriteLine($"V2 foundation checks: {passed} passed");
}
finally { db.ChangeTracker.Clear(); await TestDatabase.DeleteOwnedAsync(db, prefix, database); }

sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 1, 6, 0, 0, 0, TimeSpan.Zero); }
