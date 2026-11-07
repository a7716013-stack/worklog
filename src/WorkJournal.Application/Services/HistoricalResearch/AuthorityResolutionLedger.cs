using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.HistoricalResearch.Resolution;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

// Append-only operational ledger. Capturing a source does not grant any claim approval.
public sealed class AuthorityResolutionLedger(string root, string evidenceRoot)
{
    private string TaskFolder(Guid id) => Path.Combine(root, "resolution-queue", id.ToString("N"));
    public void Register(AuthorityResolutionTask task)
    {
        if (task.ResolutionTaskId == Guid.Empty || task.TargetStart > task.TargetEnd || string.IsNullOrWhiteSpace(task.RequiredClaimId)) throw new ArgumentException("Explicit task required.");
        var dir = TaskFolder(task.ResolutionTaskId); Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "task.json");
        if (File.Exists(file)) { if (ResearchMath.Digest(HistoricalDatasetStore.Read<AuthorityResolutionTask>(file)) != ResearchMath.Digest(task)) throw new InvalidDataException("Task cannot be mutated."); }
        else HistoricalDatasetStore.WriteNew(file, task);
    }
    public EvidenceAcquisitionAttempt[] Attempts(Guid taskId)
    {
        var dir = Path.Combine(TaskFolder(taskId), "attempts");
        if (!Directory.Exists(dir)) return [];
        var attempts = Directory.GetFiles(dir, "*.result.json").Order(StringComparer.Ordinal).Select(HistoricalDatasetStore.Read<EvidenceAcquisitionAttempt>).ToArray();
        foreach (var a in attempts)
        {
            if (a.ResolutionTaskId != taskId || a.Digest != ResearchMath.Digest(a with { Digest = "" })) throw new InvalidDataException("Acquisition ledger changed.");
            if (a.EvidenceId.HasValue && new EvidenceAcquisitionStore(evidenceRoot).ReadArtifact(a.EvidenceId.Value).SHA256 != a.RawDigest) throw new InvalidDataException("Acquisition archive changed.");
        }
        return attempts;
    }
    public AuthorityResolutionTask State(Guid id)
    {
        var task = HistoricalDatasetStore.Read<AuthorityResolutionTask>(Path.Combine(TaskFolder(id), "task.json"));
        var attempts = Attempts(id); var last = attempts.OrderBy(a => a.RequestedAt).LastOrDefault();
        var attemptDir = Path.Combine(TaskFolder(id), "attempts");
        var searching = Directory.Exists(attemptDir) ? Directory.GetFiles(attemptDir, "*.started.json") : [];
        var negative = Directory.GetFiles(TaskFolder(id), "*.search.json").Select(HistoricalDatasetStore.Read<EvidenceSearchResult>).OrderBy(x => x.SearchDate).LastOrDefault();
        return task with { AttemptCount = searching.Length, LastAttemptAt = last?.RequestedAt,
            CurrentEvidenceIds = task.CurrentEvidenceIds.Concat(attempts.Where(a => a.EvidenceId.HasValue).Select(a => a.EvidenceId!.Value)).Distinct().ToArray(),
            AcquisitionStatus = last?.ResultStatus ?? task.AcquisitionStatus,
            ReviewStatus = negative is null ? task.ReviewStatus : ResolutionStatus.BLOCKED_EXTERNAL,
            BlockingReason = negative?.Reason ?? task.BlockingReason,
            UpdatedAt = negative?.SearchDate ?? last?.ReceivedAt ?? last?.RequestedAt ?? task.UpdatedAt };
    }
    public async Task<EvidenceAcquisitionAttempt> Acquire(AuthorityResolutionTask task, string source, string op, HttpClient client, TimeProvider clock, CancellationToken token=default)
    {
        if (string.IsNullOrWhiteSpace(op)) throw new ArgumentException("Operator required.");
        Register(task); var dir = Path.Combine(TaskFolder(task.ResolutionTaskId), "attempts"); Directory.CreateDirectory(dir);
        var id = Guid.NewGuid(); var started = clock.GetUtcNow();
        var initial = new EvidenceAcquisitionAttempt(id, task.ResolutionTaskId, source, task.TargetProvider, started, null, "REQUEST_STARTED", null, null, null,
            ResolutionStatus.SEARCHING, null, op, null, "");
        HistoricalDatasetStore.WriteNew(Path.Combine(dir, id.ToString("N") + ".started.json"), initial with { Digest = ResearchMath.Digest(initial) });
        EvidenceAcquisitionAttempt result;
        try
        {
            var acquisition = new EvidenceAcquisitionTask(Guid.NewGuid(), task.TargetProvider, task.TargetDatasetType,
                AuthorityClosureStore.EvidenceDomainFor(task.AuthorityDomain), task.TargetStart, task.TargetEnd,
                AvailabilityEvidenceType.OfficialSpecification, AcquisitionStatus.Pending, 0, null, "", source, null, "");
            var m = await new EvidenceAcquisitionStore(evidenceRoot).Acquire(new(Guid.NewGuid(), task.TargetProvider, task.TargetStart, task.TargetEnd, [acquisition]), client, clock, token);
            var a = m.CapturedArtifacts.SingleOrDefault(); var failure = m.FailedArtifacts.FirstOrDefault()?.Reason;
            result = initial with { ReceivedAt = clock.GetUtcNow(), AcquisitionStatus = a is null ? failure ?? "ACQUISITION_FAILED" : "HTTP_SUCCESS",
                ContentType = a?.ContentType, RawDigest = a?.SHA256, ArchiveLocation = a is null ? null : Path.Combine(evidenceRoot, "evidence", "authority", a.EvidenceId.ToString("N"), "raw.bin"),
                ResultStatus = a is null ? ResolutionStatus.BLOCKED_EXTERNAL : ResolutionStatus.CAPTURED, FailureReason = failure, EvidenceId = a?.EvidenceId };
        }
        catch (Exception e) when (e is IOException or ArgumentException or HttpRequestException or InvalidOperationException)
        { result = initial with { ReceivedAt = clock.GetUtcNow(), AcquisitionStatus = "ACQUISITION_FAILED", ResultStatus = ResolutionStatus.BLOCKED_EXTERNAL, FailureReason = e.GetType().Name + ":" + e.Message }; }
        result = result with { Digest = ResearchMath.Digest(result) };
        HistoricalDatasetStore.WriteNew(Path.Combine(dir, id.ToString("N") + ".result.json"), result); return result;
    }
    public void NotFound(EvidenceSearchResult result)
    {
        if (result.SourcesChecked.Length == 0 || result.SourcesChecked.Any(string.IsNullOrWhiteSpace) || string.IsNullOrWhiteSpace(result.Reason) ||
            string.IsNullOrWhiteSpace(result.SearchScope) || result.ClaimState != "UNPROVEN" || result.Result != "NOT_FOUND") throw new ArgumentException("Negative search is unproven, never false.");
        var dir = TaskFolder(result.ResolutionTaskId);
        if (!File.Exists(Path.Combine(dir, "task.json"))) throw new ArgumentException("Register task first.");
        HistoricalDatasetStore.WriteNew(Path.Combine(dir, Guid.NewGuid().ToString("N") + ".search.json"), result);
    }
}
