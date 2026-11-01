using System.Reflection;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.HistoricalResearch.Components;
using WorkJournal.Web.Models.HistoricalResearch.Resolution;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

public sealed class AuthorityResolutionStore(string root, string evidenceRoot, string authorityRoot, string closureRoot)
{
    private ComponentDatasetStore Components => new(Path.Combine(root, "components"), evidenceRoot, authorityRoot, closureRoot);
    public static string CodeSHA => typeof(AuthorityResolutionStore).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+').LastOrDefault() ?? "UNKNOWN";
    public static void CheckCode(string sha)
    {
        if (sha.Length != 40 || !sha.All(Uri.IsHexDigit) || !sha.Equals(CodeSHA, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CodeSHA must match compiled SourceRevisionId.");
    }
    private string Folder(Guid id) => Path.Combine(root, "approvals", id.ToString("N"));
    private static void Write(string path, object value) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); HistoricalDatasetStore.WriteNew(path, value); }
    public AuthorityResolutionReport Evaluate(TrendResolutionRequest r) => new AuthorityResolution(evidenceRoot, authorityRoot, closureRoot).Evaluate(r);
    public AuthorityResolutionReport Execute(TrendResolutionRequest r)
    {
        var report = Evaluate(r); var dir = Path.Combine(root, "executions", Guid.NewGuid().ToString("N"));
        Write(Path.Combine(dir, "request.json"), r); Write(Path.Combine(dir, "report.json"), report);
        Write(Path.Combine(dir, "dependency-manifest.json"), Dependency(r));
        Write(Path.Combine(dir, "coverage-receipts.json"), CoverageReceipts(r, report));
        var ledger = new AuthorityResolutionLedger(root, evidenceRoot);
        foreach (var task in report.Queue) ledger.Register(task);
        Write(Path.Combine(dir, "operational-queue.json"), report.Queue.Select(t => ledger.State(t.ResolutionTaskId)).ToArray());
        return report;
    }
    private static ComponentAuthorityDependencyManifest Dependency(TrendResolutionRequest r)
    {
        var m = ComponentEvidence.Dependencies(EvidenceCode.Trend, r.Input.Request.ParameterSetId, r.Input.Request.RequestedAt) with { ManifestVersion = AuthorityResolution.DependencyVersion, Digest = "" };
        return m with { Digest = ResearchMath.Digest(m) };
    }
    private static object CoverageReceipts(TrendResolutionRequest r, AuthorityResolutionReport report)
    {
        var input = r.Input; var req = input.Request; var d = input.Closure.Dataset.Source.Dataset;
        bool approved(ClosureDomain domain) => report.Claims.Where(c => c.Domain == domain).All(c => c.CurrentStatus == "SUPPORTED");
        var start = d.Calendar.Sessions.IsEmpty ? req.CoverageStart : d.Calendar.Sessions.Min();
        var end = d.Calendar.Sessions.IsEmpty ? req.CoverageEnd : d.Calendar.Sessions.Max();
        var calendar = ComponentEvidence.Calendar(input.Sessions, start, end, d.Dates.Min(d => d.DataCutoff));
        var identity = new { CoverageStart = req.CoverageStart, CoverageEnd = req.CoverageEnd,
            ExpectedIdentityCount = approved(ClosureDomain.HistoricalUniverse) ? (int?)d.Universes.SelectMany(u => u.Members).Distinct().Count() : null,
            ResolvedIdentityCount = report.Coverage.Observations.Where(o => o.IdentityStatus == "SUPPORTED").Select(o => o.InstrumentId).Distinct().Count(),
            UnknownIdentityCount = report.Coverage.Observations.Count(o => o.IdentityStatus != "SUPPORTED"),
            EvidenceIds = input.Identities.Select(i => i.EvidenceId).Distinct().ToArray(), AuthorityRevisionIds = input.Closure.Closures.Select(c => c.RevisionDigest).ToArray() };
        var universes = d.Dates.Select(day => new { day.TradeDate, req.UniverseDefinitionVersion,
            ExpectedMemberCount = approved(ClosureDomain.HistoricalUniverse) ? (int?)d.Universes.Where(u => u.AsOfDate == day.TradeDate).Sum(u => u.Members.Length) : null,
            KnownMembers = report.Coverage.Observations.Where(o => o.TradeDate == day.TradeDate && o.UniverseStatus == "SUPPORTED").Select(o => o.InstrumentId).ToArray(),
            UnknownMembers = report.Coverage.Observations.Count(o => o.TradeDate == day.TradeDate && o.UniverseStatus != "SUPPORTED"),
            ResolvedMemberCount = report.Coverage.Observations.Count(o => o.TradeDate == day.TradeDate && o.UniverseStatus == "SUPPORTED"),
            EvidenceIds = input.Universes.Where(u => u.TradeDate == day.TradeDate).SelectMany(u => u.SourceEvidenceIds).Distinct().ToArray(),
            SnapshotDigest = ResearchMath.Digest(input.Universes.Where(u => u.TradeDate == day.TradeDate).ToArray()) }).ToArray();
        var sessions = new { CoverageStart = start, CoverageEnd = end,
            KnownTradingSessions = approved(ClosureDomain.TradingCalendar) ? calendar.VerifiedSessions : 0,
            KnownClosures = approved(ClosureDomain.TradingCalendar) ? calendar.ScheduledClosures + calendar.EmergencyClosures : 0,
            UnknownDates = approved(ClosureDomain.TradingCalendar) ? calendar.UnknownSessions : end.DayNumber - start.DayNumber + 1,
            EvidenceIds = input.Sessions.Select(s => s.SourceEvidenceId).Distinct().ToArray(), CalendarVersion = d.Calendar.Version };
        return new { ReceiptStatus = "COVERAGE_REPORT_NOT_APPROVAL", InstrumentIdentityCoverageReceipt = new { Data = identity, CoverageDigest = ResearchMath.Digest(identity) },
            HistoricalUniverseCoverageReceipt = universes, HistoricalCalendarCoverageReceipt = new { Data = sessions, Digest = ResearchMath.Digest(sessions) } };
    }
    public ResolutionApprovalReceipt Approve(TrendResolutionRequest r, string codeSha)
    {
        AuthorityResolutionReport? report = null;
        try
        {
            report = Execute(r);
            if (report.Status != "READY_FOR_EXPLICIT_APPROVAL") throw new InvalidOperationException("BLOCKED_BY_AUTHORITY_OR_DATA");
            CheckCode(codeSha);
            if (r.Input.DataMode != ComponentDataMode.RealApproved) throw new InvalidOperationException("Real receipt required.");
            var c = Components.Approve(r.Input); var d = r.Input.Closure.Dataset.Source.Dataset;
            var receipt = new ResolutionApprovalReceipt(c.ApprovalId, r.RequestId, r.Input.DataMode, c.Component, c.CandidateVersion, c.ParameterSetId,
                c.CoverageStart, c.CoverageEnd, r.Input.Request.UniverseDefinitionVersion, c.UniverseSnapshotIds, d.Calendar.Version, c.DatasetSnapshotId,
                r.Availability.Select(a => a.PolicyVersion).Distinct().Order(StringComparer.Ordinal).ToArray(), r.Revisions.Select(p => p.PolicyVersion).Distinct().Order(StringComparer.Ordinal).ToArray(),
                c.PriceBasisVersion, c.AuthorityClosureRevisions, c.EvidenceDigests,
                r.ReplaySemantics, Dependency(r).Digest, r.RequestDigest, report.Coverage.Digest, c.ApprovalDigest,
                codeSha, c.ApprovedAt, c.ApprovedBy, "");
            // EvidenceDigests contains byte digests, never GUIDs masquerading as content hashes.
            var extra = r.Availability.Select(a => a.EvidenceId).Concat(r.Revisions.Select(p => p.EvidenceId)).Distinct()
                .Select(id => new EvidenceAcquisitionStore(evidenceRoot).ReadArtifact(id).SHA256);
            receipt = receipt with { EvidenceDigests = c.EvidenceDigests.Concat(extra).Distinct().Order(StringComparer.Ordinal).ToArray() };
            receipt = receipt with { ApprovalDigest = ResearchMath.Digest(receipt) };
            Write(Path.Combine(Folder(c.ApprovalId), "archive.json"), new ResolutionApprovalArchive(r, report, receipt));
            Replay(c.ApprovalId);
            var milestone = new RealComponentMilestone("FIRST_APPROVED_REAL_COMPONENT_DATASET", c.ApprovalId, c.Component, codeSha, c.ApprovedAt, receipt.ApprovalDigest, "");
            milestone = milestone with { Digest = ResearchMath.Digest(milestone) };
            var path = Path.Combine(root, "milestones", "first-real-component.json");
            if (!File.Exists(path)) Write(path, milestone); // CreateNew also prevents concurrent replacement.
            else ReadMilestone();
            return receipt;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            Write(Path.Combine(root, "failures", Guid.NewGuid().ToString("N") + ".json"), new ComponentDatasetApprovalFailure(r.RequestId, r.Input.Request.Component,
                report?.BlockingReasons ?? [e.Message], report?.Claims.Where(c => c.CurrentStatus != "SUPPORTED").Select(c => c.Domain + ":" + c.RequiredClaim).ToArray() ?? [],
                report?.Claims.SelectMany(c => c.MissingEvidence).Distinct().ToArray() ?? [], report?.Coverage, DateTimeOffset.UtcNow, codeSha, ResearchMath.Digest(r)));
            throw;
        }
    }
    public ResolutionApprovalArchive Replay(Guid id)
    {
        var c = Components.Replay(id); var a = HistoricalDatasetStore.Read<ResolutionApprovalArchive>(Path.Combine(Folder(id), "archive.json"));
        var frozen = Path.Combine(root, "components", "approvals", id.ToString("N"));
        var e = new AuthorityResolution(Path.Combine(frozen, "evidence"), Path.Combine(frozen, "authority"), Path.Combine(frozen, "closure")).Evaluate(a.Request);
        var q = a.Request.Input.Request; var d = a.Request.Input.Closure.Dataset.Source.Dataset;
        if (a.Receipt.ApprovalId != id || a.Receipt.DataMode != ComponentDataMode.RealApproved || a.Receipt.ApprovalDigest != ResearchMath.Digest(a.Receipt with { ApprovalDigest = "" }) ||
            a.Request.RequestDigest != a.Receipt.RequestDigest || ResearchMath.Digest(c.Input) != ResearchMath.Digest(a.Request.Input) ||
            c.Receipt.ApprovalDigest != a.Receipt.ComponentApprovalDigest || e.Digest != a.Report.Digest || e.Status != "READY_FOR_EXPLICIT_APPROVAL" ||
            e.Coverage.Digest != a.Receipt.EligibilityDigest || a.Receipt.CodeSHA.Length != 40 || !a.Receipt.CodeSHA.All(Uri.IsHexDigit) ||
            a.Receipt.RequestId != a.Request.RequestId || a.Receipt.Component != q.Component || a.Receipt.CandidateVersion != q.CandidateVersion ||
            a.Receipt.ParameterSetId != q.ParameterSetId || a.Receipt.CalendarVersion != d.Calendar.Version || a.Receipt.PriceBasisVersion != q.PriceBasisVersion ||
            a.Receipt.UniverseDefinitionVersion != q.UniverseDefinitionVersion || a.Receipt.DatasetSnapshotId != c.Receipt.DatasetSnapshotId ||
            a.Receipt.CoverageStart != q.CoverageStart || a.Receipt.CoverageEnd != q.CoverageEnd || a.Receipt.ReplaySemantics != a.Request.ReplaySemantics ||
            a.Receipt.DependencyManifestDigest != Dependency(a.Request).Digest ||
            !a.Receipt.UniverseSnapshotIds.SequenceEqual(c.Receipt.UniverseSnapshotIds) || !a.Receipt.AuthorityClosureRevisionIds.SequenceEqual(c.Receipt.AuthorityClosureRevisions) ||
            !a.Receipt.AvailabilityPolicyVersions.SequenceEqual(a.Request.Availability.Select(x=>x.PolicyVersion).Distinct().Order(StringComparer.Ordinal)) ||
            !a.Receipt.RevisionPolicyVersions.SequenceEqual(a.Request.Revisions.Select(x=>x.PolicyVersion).Distinct().Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Resolution approval replay mismatch.");
        return a;
    }
    public RealComponentMilestone ReadMilestone()
    {
        var m = HistoricalDatasetStore.Read<RealComponentMilestone>(Path.Combine(root, "milestones", "first-real-component.json"));
        var receipt = Replay(m.ApprovalId).Receipt;
        if (m.Digest != ResearchMath.Digest(m with { Digest = "" }) || m.ReceiptDigest != receipt.ApprovalDigest || m.CommitSHA != receipt.CodeSHA ||
            m.Component != EvidenceCode.Trend || m.MilestoneId != "FIRST_APPROVED_REAL_COMPONENT_DATASET" || m.AchievedAt != receipt.ApprovedAt)
            throw new InvalidDataException("Milestone lineage mismatch.");
        return m;
    }
    public DatasetScopeDecision Reduce(TrendResolutionRequest original, TrendResolutionRequest next, string reason, string[] gaps, string reviewer, DateTimeOffset at)
    {
        var decision = AuthorityResolution.Reduce(original, next, reason, gaps, reviewer, at);
        var path = Path.Combine(root, "scope-decisions", next.RequestId.ToString("N"));
        Write(Path.Combine(path, "original.json"), original); Write(Path.Combine(path, "new.json"), next); Write(Path.Combine(path, "decision.json"), decision);
        return decision;
    }
    public DatasetScopeDecision ReplayScope(Guid id)
    {
        var path = Path.Combine(root, "scope-decisions", id.ToString("N"));
        var decision = HistoricalDatasetStore.Read<DatasetScopeDecision>(Path.Combine(path, "decision.json"));
        var replay = AuthorityResolution.Reduce(HistoricalDatasetStore.Read<TrendResolutionRequest>(Path.Combine(path, "original.json")),
            HistoricalDatasetStore.Read<TrendResolutionRequest>(Path.Combine(path, "new.json")), decision.Reason, decision.EvidenceGapIds, decision.ReviewedBy, decision.ReviewedAt);
        if (id != decision.NewRequestId || replay.Digest != decision.Digest) throw new InvalidDataException("Scope decision changed.");
        return replay;
    }
    public ApprovalUseRevision Revoke(Guid id, string status, string reason, DateTimeOffset at) { Replay(id); return Components.Revoke(id, status, reason, at); }
    public string Baseline(Guid id, ResearchRequest template, string sha)
    {
        ReadMilestone(); var a = Replay(id); CheckCode(sha); Components.Current(id);
        if (Evaluate(a.Request).Digest != a.Report.Digest) throw new InvalidOperationException("STALE_APPROVAL_RECEIPT");
        var dir = Components.Baseline(id, template, sha);
        var report = new RealBaselineStore(Path.Combine(root, "components", "baselines")).Replay(template.ResearchRunId);
        var metrics = report.Metrics.Where(m => m.Variable == "Trend").ToArray();
        var output = new { DatasetApprovalId = id, BaselineRunId = template.ResearchRunId, CodeSHA = sha, template.Protocol.Version,
            Approval = a.Receipt, Status = metrics.Any(m => m.DailyIc.Any(d => d.PearsonIc.HasValue || d.RankIc.HasValue)) ? "COMPLETED" : "COMPLETED_INSUFFICIENT_SCOPE",
            TradingDates = report.Evidence.Select(e => e.Date).Distinct().Count(), InstrumentCount = report.Evidence.Select(e => e.InstrumentId).Distinct().Count(), ObservationCount = report.Evidence.Length,
            FeatureDiagnostics = report.Diagnostics.Where(d => d.Component == "Trend").ToArray(), Evidence = report.Evidence,
            Metrics = metrics, DailyReasons = metrics.SelectMany(m => m.DailyIc.Select(d => new { m.Fold, m.Partition, m.Horizon, d.Date, Reason = d.PearsonIc is null || d.RankIc is null ? "INSUFFICIENT_CROSS_SECTION_OR_VARIANCE" : "COMPUTED" })).ToArray(),
            TimeStability = a.Receipt.CoverageStart.Year == a.Receipt.CoverageEnd.Year && (a.Receipt.CoverageStart.Month - 1) / 3 == (a.Receipt.CoverageEnd.Month - 1) / 3 ? "INSUFFICIENT_SCOPE" : "SEE_EXISTING_RESEARCH_GROUPS",
            Findings = report.Findings, V3Threshold = "UNRESOLVED", V3Promotion = "LOCKED", Phase5 = "LOCKED", ReplayDigest = ResearchMath.Digest(report) };
        Write(Path.Combine(root, "trend-baselines", template.ResearchRunId.ToString("N") + ".json"), output);
        Write(Path.Combine(root, "trend-baselines", template.ResearchRunId.ToString("N") + ".digest.json"), ResearchMath.Digest(output));
        return dir;
    }
    public object BaselineReplay(Guid runId)
    {
        var saved = HistoricalDatasetStore.Read<System.Text.Json.JsonElement>(Path.Combine(root, "trend-baselines", runId.ToString("N") + ".json"));
        if (ResearchMath.Digest(saved) != HistoricalDatasetStore.Read<string>(Path.Combine(root, "trend-baselines", runId.ToString("N") + ".digest.json"))) throw new InvalidDataException("Trend report changed.");
        Replay(saved.GetProperty("DatasetApprovalId").GetGuid()); Components.BaselineReplay(runId);
        var report = new RealBaselineStore(Path.Combine(root, "components", "baselines")).Replay(runId);
        if (saved.GetProperty("ReplayDigest").GetString() != ResearchMath.Digest(report)) throw new InvalidDataException("Trend baseline replay mismatch.");
        return saved;
    }
}
