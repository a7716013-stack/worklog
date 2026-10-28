using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.V2;

var count = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); count++; Console.WriteLine("PASS: " + label); }
async Task Reject(Func<Task> action, string label)
{
    try { await action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or DbUpdateException) { Check(true, label); return; }
    throw new Exception("Expected rejection: " + label);
}
var calculator = new FullEvidenceScoreCalculator();
var codes = Enum.GetValues<EvidenceCode>();
NormalizedEvidence[] Inputs(params decimal[] values) => codes.Select((code, i) => new NormalizedEvidence(code, values[i], EvidenceStatus.Available, "FIXTURE", "fixture-feature-v1", "fixture-normalization-v1")).ToArray();
Check(FullEvidenceModel.Weights.Values.Sum() == 100 && FullEvidenceModel.Weights.Count == 6, "exact six weights sum to 100");
var expectedWeights = new[] { 25, 15, 15, 20, 15, 10 };
foreach (var value in new[] { 0m, .5m, 1m })
{
    var result = calculator.Calculate(Inputs(Enumerable.Repeat(value, 6).ToArray()));
    Check(result.TotalScore == value * 100 && result.Status == EvidenceScoreStatus.Complete && result.AvailableWeight == 100, "golden total " + value * 100);
    for (var i = 0; i < 6; i++) Check(result.Components[i].Code == codes[i] && result.Components[i].Weight == expectedWeights[i] && result.Components[i].WeightedPoints == value * expectedWeights[i], "golden contribution " + value + " " + codes[i]);
}
var mixed = Inputs(1m, .8m, .6m, .4m, .2m, 0m);
var mixedScore = calculator.Calculate(mixed);
Check(mixedScore.TotalScore == 57m, "mixed golden exactly 57");
Check(mixedScore.Components.Select(x => x.WeightedPoints).SequenceEqual(new decimal?[] { 25, 12, 9, 8, 3, 0 }), "mixed exact component mapping");
Check(calculator.Calculate(mixed.Reverse()).Components.SequenceEqual(mixedScore.Components), "input ordering cannot remap evidence weights");
foreach (var status in new[] { EvidenceStatus.Missing, EvidenceStatus.Invalid, EvidenceStatus.Stale, EvidenceStatus.NotApplicable })
{
    var input = Inputs(1, 1, 1, 1, 1, 1); input[3] = input[3] with { Value = null, Status = status, ReasonCode = status.ToString() };
    var result = calculator.Calculate(input);
    Check(result.Status == EvidenceScoreStatus.Incomplete && result.TotalScore is null && result.AvailableWeight == 80, status + " does not produce a complete score");
    Check(result.Components[3].WeightedPoints is null && result.Components[3].Status == status && result.Components[3].ReasonCode == status.ToString(), status + " preserves reason/quality without zero or neutral substitution");
}
var unavailable = Inputs(0, 0, 0, 0, 0, 0).Select(x => x with { Value = null, Status = EvidenceStatus.Missing }).ToArray();
Check(calculator.Calculate(unavailable) is { AvailableWeight: 0, TotalScore: null, Status: EvidenceScoreStatus.Incomplete }, "all missing is not zero score");
var zero = calculator.Calculate(Inputs(0, 0, 0, 0, 0, 0));
Check(zero is { AvailableWeight: 100, TotalScore: 0, Status: EvidenceScoreStatus.Complete }, "real zero is available");
var precise = calculator.Calculate(Inputs(.123456789m, 0, 0, 0, 0, 0));
Check(precise.TotalScore == 3.086419725m && precise.Components[0].WeightedPoints == 3.086419725m, "no early rounding or display quantization");
foreach (var invalid in new[] { -.00001m, 1.00001m })
{
    var input = Inputs(0, 0, 0, 0, 0, 0); input[0] = input[0] with { Value = invalid };
    await Reject(() => Task.FromResult(calculator.Calculate(input)), "normalized range rejection " + invalid);
}
await Reject(() => Task.FromResult(calculator.Calculate(mixed.Take(5))), "absent component requires explicit missing status");
var duplicate = mixed.ToArray(); duplicate[5] = duplicate[0];
await Reject(() => Task.FromResult(calculator.Calculate(duplicate)), "duplicate code rejected");
var unknown = mixed.ToArray(); unknown[0] = unknown[0] with { Code = (EvidenceCode)99 };
await Reject(() => Task.FromResult(calculator.Calculate(unknown)), "unknown code rejected");
var bad = mixed.ToArray(); bad[0] = bad[0] with { Status = (EvidenceStatus)99 };
await Reject(() => Task.FromResult(calculator.Calculate(bad)), "unknown status rejected");
bad = mixed.ToArray(); bad[0] = bad[0] with { Value = null };
await Reject(() => Task.FromResult(calculator.Calculate(bad)), "available without value rejected");
bad = mixed.ToArray(); bad[0] = bad[0] with { Status = EvidenceStatus.Missing };
await Reject(() => Task.FromResult(calculator.Calculate(bad)), "missing with authoritative normalized value rejected");
bad = mixed.ToArray(); bad[0] = bad[0] with { NormalizationVersion = "" };
await Reject(() => Task.FromResult(calculator.Calculate(bad)), "normalization version required");
bad = mixed.ToArray(); bad[0] = bad[0] with { ReasonCode = "" };
await Reject(() => Task.FromResult(calculator.Calculate(bad)), "stable reason required");
var snapshot = calculator.Calculate(mixed); mixed[0] = mixed[0] with { Value = 0 };
Check(snapshot.TotalScore == 57 && snapshot.Components[0].NormalizedValue == 1, "result detached from mutable input array");
var oldVersion = snapshot.Components[0].FeatureVersion;
var changedFeature = Inputs(1, .8m, .6m, .4m, .2m, 0); changedFeature[0] = changedFeature[0] with { FeatureVersion = "fixture-feature-v2" };
var changedResult = calculator.Calculate(changedFeature);
Check(snapshot.Components[0].FeatureVersion == oldVersion && changedResult.Components[0].FeatureVersion != oldVersion && changedResult.TotalScore == 57, "definition version preserved independently from numeric score");
Check(FullEvidenceModel.Gaps.Count == 6 && FullEvidenceModel.Gaps.Select(x => x.Evidence).Distinct().Count() == 6, "all six unresolved definitions explicitly reported");
if (args.Contains("--fast")) { Console.WriteLine($"V2EvidenceChecks: {count} PASS (aggregation/contracts only)"); return; }

const string prefix = "WorkJournalPaperChecks_";
var database = prefix + Guid.NewGuid().ToString("N");
var options = new DbContextOptionsBuilder<JournalDbContext>().UseSqlServer(TestDatabase.Connection(prefix, database), sql => sql.EnableRetryOnFailure()).Options;
await using var db = new JournalDbContext(options);
var foundation = new FoundationService(db, new FixedClock());
var service = new FullEvidencePreparationService(db, foundation);
var cutoff = new DateTimeOffset(2026, 1, 5, 13, 30, 0, TimeSpan.FromHours(8));
var date = new DateOnly(2026, 1, 5);
try
{
    await db.Database.MigrateAsync();
    await TestDatabase.PrintVersionAsync(db);
    Check(!db.Database.HasPendingModelChanges(), "Phase 3 partial implementation needs no schema changes");
    var instrument = await foundation.RegisterAsync("evidence-fixture", "Fixture", "Equity", "TWD", cutoff.AddYears(-1));
    DataProvenance Record(string key, DateTimeOffset? available, EvidenceStatus quality = EvidenceStatus.Available) => new()
    {
        InstrumentId = instrument.InstrumentId, Provider = "fixture", Dataset = "raw-fixture", SourceKey = key,
        EffectiveAt = cutoff.AddDays(-20), AvailableAt = available, ObservedAt = cutoff.AddDays(1), RetrievedAt = cutoff.AddDays(1),
        QualityStatus = quality, PayloadJson = "{\"close\":100}"
    };
    DatasetSnapshot Dataset() => new() { AsOfDate = date, DataCutoff = cutoff, DatasetVersion = "data-fixture-v1", SourceSummary = "fixture", Records = [
        Record("before", cutoff.AddSeconds(-1)), Record("equal", cutoff), Record("after", cutoff.AddSeconds(1)), Record("unknown", null), Record("stale", cutoff.AddSeconds(-2), EvidenceStatus.Stale), Record("invalid", cutoff.AddSeconds(-3), EvidenceStatus.Invalid), Record("missing", cutoff.AddSeconds(-4), EvidenceStatus.Missing)] };
    var dataset = await foundation.CreateDatasetAsync(Dataset());
    var universe = await foundation.CreateUniverseAsync(new() { TradeDate = date, DataCutoff = cutoff, UniverseVersion = "universe-fixture-v1", Members = [new() { InstrumentId = instrument.InstrumentId, EligibilityStatus = UniverseEligibility.Eligible, ReasonCode = "fixture" }] });
    var definition = new BenchmarkDefinition { Id = Guid.NewGuid(), Code = "fixture-benchmark", Name = "Fixture" };
    var benchmark = new BenchmarkVersion { Id = Guid.NewGuid(), BenchmarkDefinitionId = definition.Id, Version = "fixture-v1", ValidFrom = cutoff.AddYears(-1) };
    db.AddRange(definition, benchmark, new BenchmarkMapping { Id = Guid.NewGuid(), InstrumentId = instrument.InstrumentId, BenchmarkVersionId = benchmark.Id, ValidFrom = cutoff.AddYears(-1) }); await db.SaveChangesAsync();
    var versions = await foundation.CreateVersionSetAsync(new() { ModelVersion = "unresolved-fixture-model", FeatureVersion = "unresolved-fixture-features", DataVersion = "data-fixture-v1", UniverseVersion = "universe-fixture-v1", BenchmarkVersionId = benchmark.Id });
    var parameters = await foundation.CreateParametersAsync("evidence-fixture", "v1", "{}");
    var request = new FullEvidenceRequest(instrument.InstrumentId, date, cutoff, dataset.Id, universe.Id, versions.Id, parameters.Id);
    var prepared = await service.PrepareAsync(request);
    Check(prepared.Status == "MODEL_DEFINITION_GAP" && prepared.TotalScore is null && prepared.DecisionTraceId is null && prepared.Gaps.Count == 6, "preparation never invents scores or traces when model definitions are unresolved");
    Check(prepared.PitInputs.Any(x => x.SourceKey == "before") && prepared.PitInputs.Any(x => x.SourceKey == "equal"), "PIT includes T-1 second and T");
    Check(prepared.PitInputs.All(x => x.SourceKey is not ("after" or "unknown")), "PIT excludes T+1 and unknown availability despite old effective date");
    Check(prepared.PitInputs.Any(x => x.QualityStatus == EvidenceStatus.Stale) && prepared.PitInputs.Any(x => x.QualityStatus == EvidenceStatus.Invalid) && prepared.PitInputs.Any(x => x.QualityStatus == EvidenceStatus.Missing), "PIT preparation preserves source quality rather than turning it into zero");
    string Facts(EvidencePreparation result) => CanonicalJson.Of(result.PitInputs.OrderBy(x => x.SourceKey).Select(x => new { x.SourceKey, x.PayloadJson, x.AvailableAt, x.QualityStatus }).ToArray());
    var replay = await service.PrepareAsync(request);
    Check(Facts(prepared) == Facts(replay) && prepared.ParameterChecksum == replay.ParameterChecksum, "same explicit references replay identical PIT inputs");
    var futureDataset = Dataset(); futureDataset.Records.Add(Record("future-mutation", cutoff.AddDays(10)));
    var future = await foundation.CreateDatasetAsync(futureDataset);
    Check(Facts(prepared) == Facts(await service.PrepareAsync(request with { DatasetSnapshotId = future.Id })), "future published facts cannot change prepared historical inputs");
    var newerBenchmark = new BenchmarkVersion { Id = Guid.NewGuid(), BenchmarkDefinitionId = definition.Id, Version = "fixture-v2", ValidFrom = cutoff.AddDays(1) };
    db.AddRange(newerBenchmark, new BenchmarkMapping { Id = Guid.NewGuid(), InstrumentId = instrument.InstrumentId, BenchmarkVersionId = newerBenchmark.Id, ValidFrom = cutoff.AddDays(1) }); await db.SaveChangesAsync();
    Check((await service.PrepareAsync(request)).BenchmarkVersionId == benchmark.Id, "future benchmark version does not replace explicitly selected historical version");
    var newerParams = await foundation.CreateParametersAsync("evidence-fixture", "v2", "{\"fixture\":1}");
    var withNewParams = await service.PrepareAsync(request with { ParameterSetId = newerParams.Id });
    Check(withNewParams.ParameterChecksum != prepared.ParameterChecksum && (await service.PrepareAsync(request)).ParameterChecksum == prepared.ParameterChecksum, "new parameter reference does not mutate historical parameter binding");
    await Reject(() => service.PrepareAsync(request with { DataCutoff = cutoff.AddSeconds(1) }), "different cutoff rejected");
    await Reject(() => service.PrepareAsync(request with { TradeDate = date.AddDays(1) }), "different trade date rejected");
    await Reject(() => service.PrepareAsync(request with { InstrumentId = Guid.NewGuid() }), "instrument outside research universe rejected");
    await Reject(() => service.PrepareAsync(request with { ParameterSetId = Guid.NewGuid() }), "unknown parameter reference rejected");
    var excludedUniverse = await foundation.CreateUniverseAsync(new() { TradeDate = date, DataCutoff = cutoff, UniverseVersion = "universe-fixture-v1", Members = [new() { InstrumentId = instrument.InstrumentId, EligibilityStatus = UniverseEligibility.Ineligible, ReasonCode = "fixture-excluded" }] });
    await Reject(() => service.PrepareAsync(request with { UniverseSnapshotId = excludedUniverse.Id }), "ineligible historical universe is not silently scored");
    Check(await db.Set<DecisionTrace>().CountAsync() == 0 && !db.ChangeTracker.HasChanges(), "preparation is read-only and does not persist fake scores or decisions");
    Console.WriteLine($"V2EvidenceChecks: {count} PASS (partial Phase 3: aggregation/contracts/PIT preparation; normalization unresolved)");
}
finally { db.ChangeTracker.Clear(); await TestDatabase.DeleteOwnedAsync(db, prefix, database); }
sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 1, 6, 0, 0, 0, TimeSpan.Zero); }
