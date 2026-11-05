using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.HistoricalResearch.Components;
using WorkJournal.Web.Models.HistoricalResearch.Resolution;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

// Phase 4F-2 operational resolution of the existing closure contract; no independent authority registry.
public sealed class AuthorityResolution(string evidenceRoot, string authorityRoot, string closureRoot)
{
    public const string DependencyVersion = "TrendAuthorityDependency-v1";
    public static TrendResolutionRequest Create(ComponentDatasetInput input)
    {
        var id = new Guid(Convert.FromHexString(ResearchMath.Digest(new { Input = input, DependencyVersion }))[..16]);
        var r = new TrendResolutionRequest(id, input, DependencyVersion, ReplaySemantics.MarketAvailableReplay, [], [], null, "");
        return r with { RequestDigest = ResearchMath.Digest(r) };
    }
    public static void Validate(TrendResolutionRequest r)
    {
        if (r.RequestId == Guid.Empty || r.RequestDigest != ResearchMath.Digest(r with { RequestDigest = "" }) ||
            r.Input.Request.RequestDigest != ResearchMath.Digest(r.Input.Request with { RequestDigest = "" }) ||
            r.Input.Request.Component != EvidenceCode.Trend || r.DependencyVersion != DependencyVersion ||
            r.ReplaySemantics == ReplaySemantics.Unknown || !Enum.IsDefined(r.ReplaySemantics))
            throw new InvalidDataException("Explicit frozen Trend resolution request required.");
    }
    public static AvailabilityResolution Availability(HistoricalFact f, ResolutionAvailability? a, EvidenceReviewReceipt? review, DateTimeOffset cutoff)
    {
        if (a is null || review is null || a.FactId != f.FactChecksum || a.Provider != f.Provider || a.DatasetType != f.DatasetType ||
            a.EvidenceId != review.EvidenceId || string.IsNullOrWhiteSpace(a.PolicyVersion) || !Enum.IsDefined(a.ClaimType) ||
            a.ClaimType == PublicationClaimType.QueryServiceAvailability || !review.Supported.Contains(a.ClaimType.ToString()))
            return new(null, "UNVERIFIABLE_AVAILABILITY", a?.PolicyVersion ?? "UNKNOWN");
        var resolved = EvidenceAcquisitionStore.Resolve(a.Rule, review, f.TradeDate);
        if (resolved.AvailableAt is null) return resolved;
        if (resolved.AvailableAt < f.EffectiveAt || resolved.AvailableAt > f.ObservedAt || f.ObservedAt > f.RetrievedAt)
            return new(null, "INVALID_PUBLICATION_ORDER", a.PolicyVersion);
        return resolved with { Reason = resolved.AvailableAt > cutoff ? "FUTURE_KNOWN" : "ELIGIBLE" };
    }
    public static ObservationStatus Revision(HistoricalFact f, ResolutionRevision? p, EvidenceReviewReceipt? review, DateTimeOffset cutoff)
    {
        if (p is null || review is null || p.FactId != f.FactChecksum || p.Provider != f.Provider || p.DatasetType != f.DatasetType ||
            p.EvidenceId != review.EvidenceId || p.ReviewDigest != review.ReviewDigest ||
            review.ReviewDigest != ResearchMath.Digest(review with { ReviewDigest = "" }) ||
            !review.Supported.Contains("RevisionBehavior") || !review.Supported.Contains("Revision:" + p.Semantics) ||
            review.Unsupported.Contains("RevisionBehavior") || review.Ambiguous.Contains("RevisionBehavior") ||
            review.Unsupported.Contains("Revision:" + p.Semantics) || review.Ambiguous.Contains("Revision:" + p.Semantics) ||
            f.TradeDate < p.EffectiveFrom || (p.EffectiveTo.HasValue && f.TradeDate > p.EffectiveTo) ||
            p.EffectiveFrom < review.EffectiveFrom || !p.EffectiveTo.HasValue || p.EffectiveTo > review.EffectiveTo ||
            string.IsNullOrWhiteSpace(p.PolicyVersion) || !Enum.IsDefined(p.Semantics) ||
            p.Semantics is RevisionSemantics.UNKNOWN or RevisionSemantics.LATEST_ONLY_SOURCE or RevisionSemantics.CORRECTABLE_WITHOUT_TIMESTAMP ||
            !ComponentEvidence.ProviderRevision(p.SelectedRevisionId, f.SourceChecksum) || p.SelectedRevisionId != f.RevisionId ||
            p.RevisionAvailableAt is null || p.RevisionAvailableAt < f.EffectiveAt || p.RevisionAvailableAt > f.ObservedAt ||
            (p.Semantics != RevisionSemantics.IMMUTABLE_SOURCE && !p.HistoricalVersionRetained))
            return ObservationStatus.UNVERIFIABLE_REVISION;
        return p.RevisionAvailableAt > cutoff ? ObservationStatus.FUTURE_KNOWN : ObservationStatus.ELIGIBLE;
    }
    private EvidenceReviewReceipt? Review(string? digest, string provider, HistoricalDatasetType type, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        try
        {
            var store = new EvidenceAcquisitionStore(evidenceRoot); var r = store.ReadReview(digest); var a = store.ReadArtifact(r.EvidenceId);
            return a.Provider == provider && r.ReviewedAt <= at &&
                (a.DatasetType == type.ToString() || a.DatasetType == "Availability" || a.DatasetType == "HistoricalPriceVolume") ? r : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or InvalidOperationException) { return null; }
    }
    public AuthorityResolutionReport Evaluate(TrendResolutionRequest r)
    {
        Validate(r); var input = r.Input; var d = input.Closure.Dataset.Source.Dataset; var request = input.Request;
        var component = new ComponentDatasetGate(evidenceRoot, authorityRoot, closureRoot).Evaluate(input);
        var claims = new List<ResolutionClaimRow>(); var queue = new List<AuthorityResolutionTask>();
        var closure = new AuthorityClosureStore(closureRoot, evidenceRoot);
        foreach (var domain in Enum.GetValues<ClosureDomain>())
        {
            var binding = input.Closure.Closures.SingleOrDefault(c => c.Domain == domain);
            ClosureRevision? current = null;
            try { if (binding is not null) current = closure.Read(binding.CaseId); }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or InvalidOperationException) { }
            foreach (var claim in AuthorityClosureStore.RequiredClaims(domain, false))
            {
                var decision = current?.Claims.SingleOrDefault(c => c.Claim == claim);
                var scoped = current is not null && binding is not null && current.Digest == binding.RevisionDigest &&
                    current.Case.SubjectDigest == AuthorityClosureStore.Subject(d, domain) && current.Case.TargetStart <= request.CoverageStart && current.Case.TargetEnd >= request.CoverageEnd;
                var status = scoped && current!.Case.CurrentStatus == ClosureStatus.APPROVED && decision?.Status == "SUPPORTED" ? "SUPPORTED" :
                    decision?.Status == "CONFLICTING" ? "CONFLICTING" : decision?.Status is "SUPPORTED" or "PARTIALLY_SUPPORTED" ? "PARTIALLY_SUPPORTED" : "UNKNOWN";
                var ids = current?.Case.EvidenceIds ?? [];
                var missing = status == "SUPPORTED" ? Array.Empty<string>() : new[] { scoped ? "REQUIRED_CLAIM_NOT_APPROVED" : "SCOPED_CURRENT_CLOSURE_REQUIRED", claim };
                var next = status == "SUPPORTED" ? "Preserve frozen evidence and closure revision" : "Acquire historical evidence for " + claim + "; review and bind to this exact subject and period";
                claims.Add(new(domain, claim, status, ids, missing, request.CoverageStart, request.CoverageEnd,
                    status == "CONFLICTING" ? "CONFLICTING" : "NONE_RECORDED", next));
                var id = new Guid(Convert.FromHexString(ResearchMath.Digest(new { r.RequestId, domain, claim }))[..16]);
                queue.Add(new(id, EvidenceCode.Trend, domain, claim, current?.Case.Provider ?? "TWSE", current?.Case.DatasetType ?? domain.ToString(),
                    request.CoverageStart, request.CoverageEnd, ids, string.Join(';', missing), ids.Length > 0 ? ResolutionStatus.CAPTURED : ResolutionStatus.PENDING,
                    status == "SUPPORTED" ? ResolutionStatus.SUPPORTED : status == "PARTIALLY_SUPPORTED" ? ResolutionStatus.PARTIALLY_SUPPORTED : status == "CONFLICTING" ? ResolutionStatus.CONFLICTING : ResolutionStatus.PENDING,
                    string.Join(';', missing), 0, null, null, null, request.RequestedAt, request.RequestedAt));
            }
        }
        bool Approved(ClosureDomain domain) => claims.Where(c => c.Domain == domain).All(c => c.CurrentStatus == "SUPPORTED");
        var observations = new List<HistoricalObservationEligibility>(); var matrix = new List<AvailabilityCoverageRow>(); int? expected = 0;
        var parsedRows = new Dictionary<Guid, ParsedPriceRow[]>();
        bool Lineage(HistoricalFact fact, Guid instrument)
        {
            var matches = input.Lineage.Where(l => l.FactId == fact.FactChecksum).ToArray();
            if (matches.Length != 1) return false;
            var l = matches[0];
            try
            {
                if (!parsedRows.ContainsKey(l.RawEvidenceId)) parsedRows[l.RawEvidenceId] = ComponentEvidence.ParsePrice(evidenceRoot, l.RawEvidenceId);
                var row = parsedRows[l.RawEvidenceId].SingleOrDefault(x => x.Locator == l.RawRecordLocator);
                return row is not null && row.Value == fact.Value && row.ParsedValueDigest == l.ParsedValueDigest &&
                    l.InstrumentId == instrument && l.DatasetSnapshotId == component.DatasetSnapshotId && l.RevisionIdentity == fact.RevisionId &&
                    l.TradeDate == fact.TradeDate && l.RawRecordLocator.ResponseDigest == fact.SourceChecksum && l.RawRecordLocator.Symbol == fact.SourceInstrumentKey &&
                    l.AvailableAtPolicyId == fact.AvailabilityPolicyId && l.ParserVersion == ComponentEvidence.ParserVersion;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException) { return false; }
        }
        foreach (var day in d.Dates)
        {
            var universes = d.Universes.Where(u => u.AsOfDate == day.TradeDate).ToArray();
            var universe = universes.Length == 1 ? universes[0] : null;
            var universeValid = Approved(ClosureDomain.HistoricalUniverse) && universe is not null && universe.AvailableAt <= day.DataCutoff &&
                universe.DefinitionVersion == request.UniverseDefinitionVersion && universe.Checksum == HistoricalAvailability.UniverseChecksum(universe);
            if (!universeValid) expected = null; else if (expected.HasValue) expected += universe!.Members.Length;
            var symbols = d.Facts.Where(f => f.DatasetType == HistoricalDatasetType.Price && f.TradeDate <= day.TradeDate).Select(f => f.SourceInstrumentKey)
                .Concat(d.Instruments.Where(m => universe?.Members.Contains(m.InstrumentId) == true).Select(m => m.SourceInstrumentKey)).Distinct().Order(StringComparer.Ordinal);
            foreach (var symbol in symbols)
            {
                var reasons = new List<string>();
                var ids = input.Identities.Where(i => i.Symbol == symbol && i.ValidFrom <= day.DataCutoff && (!i.ValidTo.HasValue || i.ValidTo > day.DataCutoff) &&
                    i.AvailableAt <= day.DataCutoff && i.InstrumentId != Guid.Empty).ToArray();
                var id = ids.Length == 1 ? ids[0].InstrumentId : Guid.Empty;
                var identity = Approved(ClosureDomain.HistoricalInstrumentIdentity) && id != Guid.Empty;
                var member = universeValid && universe!.Members.Contains(id);
                var calendarStart = d.Calendar.Sessions.IsEmpty ? request.CoverageStart : d.Calendar.Sessions.Min();
                var calendarEnd = d.Calendar.Sessions.IsEmpty ? request.CoverageEnd : d.Calendar.Sessions.Max();
                var calendar = Approved(ClosureDomain.TradingCalendar) && ComponentEvidence.Calendar(input.Sessions, calendarStart, calendarEnd, day.DataCutoff).UnknownSessions == 0;
                var facts = d.Facts.Where(f => f.DatasetType == HistoricalDatasetType.Price && f.SourceInstrumentKey == symbol && f.TradeDate <= day.TradeDate).ToArray();
                var price = facts.Length > 0 && facts.Any(f => f.TradeDate == day.TradeDate) && facts.All(f => f.Value.Close.HasValue);
                var invalid = facts.Any(f => f.Value.Close <= 0 || f.FactChecksum != HistoricalAvailability.FactChecksum(f) || f.QualityStatus == EvidenceStatus.Invalid);
                var avail = true; var revision = true; var future = false;
                foreach (var group in facts.GroupBy(f => f.TradeDate))
                {
                    var candidates = new List<(HistoricalFact Fact, DateTimeOffset At)>();
                    bool groupAvailability = false, groupRevision = false, groupFuture = false;
                    foreach (var f in group)
                    {
                        var aa = r.Availability.Where(a => a.FactId == f.FactChecksum).ToArray(); var a = aa.Length == 1 ? aa[0] : null;
                        var pp = r.Revisions.Where(p => p.FactId == f.FactChecksum).ToArray(); var p = pp.Length == 1 ? pp[0] : null;
                        var av = Availability(f, a, Review(a?.Rule.ReviewDigest, f.Provider, f.DatasetType, request.RequestedAt), day.DataCutoff);
                        var rev = Revision(f, p, Review(p?.ReviewDigest, f.Provider, f.DatasetType, request.RequestedAt), day.DataCutoff);
                        matrix.Add(new(f.Provider, f.DatasetType, f.TradeDate, a?.ClaimType.ToString() ?? "UNKNOWN", a?.Rule.Precision ?? AvailabilityPrecision.Unknown,
                            av.AvailableAt.HasValue ? "EVIDENCE_BOUND" : "UNKNOWN", a is null ? [] : [a.EvidenceId], a?.PolicyVersion ?? "UNKNOWN", av.AvailableAt.HasValue ? "SUPPORTED" : "UNKNOWN"));
                        // Resolution must match the frozen facts consumed by the existing PIT builder. Never silently rewrite a fact or manufacture precision.
                        var old = HistoricalAvailability.Resolve(f, d.Policies.SingleOrDefault(x => x.PolicyId == f.AvailabilityPolicyId));
                        bool aok = av.AvailableAt.HasValue && old.At == av.AvailableAt && a?.PolicyVersion == d.Policies.SingleOrDefault(x => x.PolicyId == f.AvailabilityPolicyId)?.Version;
                        groupAvailability |= aok; groupRevision |= rev == ObservationStatus.ELIGIBLE;
                        groupFuture |= av.Reason == "FUTURE_KNOWN" || rev == ObservationStatus.FUTURE_KNOWN;
                        if (aok && av.Reason == "ELIGIBLE" && rev == ObservationStatus.ELIGIBLE && p!.RevisionAvailableAt == av.AvailableAt) candidates.Add((f, av.AvailableAt!.Value));
                    }
                    if (candidates.Count == 0) { avail &= groupAvailability; revision &= groupRevision; future |= groupFuture; if(groupAvailability && groupRevision && !groupFuture) revision = false; }
                    else if (candidates.Count(c => c.At == candidates.Max(x => x.At)) != 1) { invalid = true; reasons.Add("CONFLICTING_LATEST_REVISION"); }
                    else if (!Lineage(candidates.MaxBy(c => c.At).Fact, id)) { invalid = true; reasons.Add("RAW_FACT_LINEAGE_GAP"); }
                }
                if (facts.Length == 0) { avail = false; revision = false; }
                var basis = d.PricePolicy.Frozen && facts.All(f => f.PriceBasisVersion == request.PriceBasisVersion) &&
                    new[] { "RAW", "ADJUSTED", "TOTAL_RETURN" }.Contains(d.PricePolicy.Basis.ToUpperInvariant());
                var actions = Approved(ClosureDomain.CorporateActionPriceBasis) && input.CorporateActions.Where(a => a.InstrumentId == id &&
                    (!a.EffectiveDate.HasValue || a.EffectiveDate >= calendarStart && a.EffectiveDate <= calendarEnd)).All(a => a.EffectiveDate.HasValue && a.ExDate.HasValue &&
                    a.AvailableAt <= day.DataCutoff && !string.IsNullOrWhiteSpace(a.ActionType) && a.SourceEvidenceId != Guid.Empty &&
                    (d.PricePolicy.Basis.Equals("RAW", StringComparison.OrdinalIgnoreCase) || a.AdjustmentFactor > 0));
                if (!identity) reasons.Add(nameof(ObservationStatus.UNRESOLVED_IDENTITY));
                if (!calendar) reasons.Add(nameof(ObservationStatus.UNRESOLVED_CALENDAR));
                if (!member) reasons.Add(nameof(ObservationStatus.UNRESOLVED_UNIVERSE));
                if (!price) reasons.Add(nameof(ObservationStatus.MISSING));
                if (invalid) reasons.Add(nameof(ObservationStatus.INVALID));
                if (!avail) reasons.Add(nameof(ObservationStatus.UNVERIFIABLE_AVAILABILITY));
                if (!revision) reasons.Add(nameof(ObservationStatus.UNVERIFIABLE_REVISION));
                if (future) reasons.Add(nameof(ObservationStatus.FUTURE_KNOWN));
                if (!basis) reasons.Add(nameof(ObservationStatus.PRICE_BASIS_CONFLICT));
                if (!actions) reasons.Add(nameof(ObservationStatus.CORPORATE_ACTION_UNRESOLVED));
                if (!Approved(ClosureDomain.HistoricalPriceVolume)) reasons.Add("PRICE_AUTHORITY_UNPROVEN");
                var final = FinalStatus(reasons);
                observations.Add(new(id, day.TradeDate, symbol, identity ? "SUPPORTED" : "UNKNOWN", calendar ? "SUPPORTED" : "UNKNOWN", member ? "SUPPORTED" : "UNKNOWN",
                    invalid ? "INVALID" : price ? "AVAILABLE" : "MISSING", avail ? "SUPPORTED" : "UNKNOWN", revision ? "SUPPORTED" : "UNKNOWN", basis && actions ? "SUPPORTED" : "UNKNOWN",
                    final, reasons.Distinct().ToArray(), ResearchMath.Digest(new { r.RequestDigest, day, symbol, facts })));
            }
            // Missing member with no symbol mapping must still be counted, never silently removed from the requested universe.
            foreach (var id in universe?.Members.Where(id => !observations.Any(o => o.TradeDate == day.TradeDate && o.InstrumentId == id)) ?? [])
                observations.Add(new(id, day.TradeDate, "UNKNOWN", "UNKNOWN", "UNKNOWN", universeValid ? "SUPPORTED" : "UNKNOWN", "MISSING", "UNKNOWN", "UNKNOWN", "UNKNOWN",
                    ObservationStatus.UNRESOLVED_IDENTITY, ["UNRESOLVED_IDENTITY", "MISSING"], ResearchMath.Digest(new { r.RequestDigest, day, id })));
        }
        int eligible = observations.Count(o => o.FinalStatus == ObservationStatus.ELIGIBLE);
        var coverage = new ResolutionCoverage(expected, eligible, observations.Count - eligible, expected > 0 ? (decimal)eligible / expected : null, observations.ToArray(), "");
        coverage = coverage with { Digest = ResearchMath.Digest(coverage) };
        var gaps = component.KnownGaps.Concat(claims.Where(c => c.CurrentStatus != "SUPPORTED").Select(c => c.Domain + ":" + c.RequiredClaim))
            .Concat(observations.SelectMany(o => o.BlockingReasons)).ToList();
        if (expected is null || expected == 0 || eligible != expected) gaps.Add("REQUESTED_OBSERVATIONS_NOT_FULLY_ELIGIBLE");
        if (r.ReplaySemantics == ReplaySemantics.SystemKnownReplay) gaps.Add("SYSTEM_KNOWN_REPLAY_NOT_IMPLEMENTED_REQUIRES_HISTORICAL_INGESTION_PROOF");
        if (r.Availability.Any(a => !d.Facts.Any(f => f.FactChecksum == a.FactId)) || r.Revisions.Any(p => !d.Facts.Any(f => f.FactChecksum == p.FactId))) gaps.Add("RESOLUTION_FACT_SCOPE_MISMATCH");
        var report = new AuthorityResolutionReport(r.RequestId, gaps.Count == 0 ? "READY_FOR_EXPLICIT_APPROVAL" : "BLOCKED_BY_AUTHORITY_OR_DATA", claims.ToArray(),
            queue.OrderBy(q => q.RequiredClaimId == "HistoricalPublication" ? 0 : q.RequiredClaimId == "RevisionBehavior" ? 1 : 2).ToArray(),
            matrix.DistinctBy(ResearchMath.Digest).ToArray(), coverage, component, gaps.Distinct().Order(StringComparer.Ordinal).ToArray(),
            claims.Count == 0 ? null : (decimal)claims.Count(c => c.CurrentStatus == "SUPPORTED") / claims.Count, ResearchMath.Digest(r), "");
        return report with { Digest = ResearchMath.Digest(report) };
    }
    public static ObservationStatus FinalStatus(IEnumerable<string> reasons)
    {
        var all = reasons.ToHashSet(StringComparer.Ordinal);
        foreach (var s in new[] { ObservationStatus.INVALID, ObservationStatus.FUTURE_KNOWN, ObservationStatus.UNVERIFIABLE_AVAILABILITY,
            ObservationStatus.UNVERIFIABLE_REVISION, ObservationStatus.UNRESOLVED_IDENTITY, ObservationStatus.UNRESOLVED_UNIVERSE,
            ObservationStatus.UNRESOLVED_CALENDAR, ObservationStatus.PRICE_BASIS_CONFLICT, ObservationStatus.CORPORATE_ACTION_UNRESOLVED, ObservationStatus.MISSING })
            if (all.Contains(s.ToString())) return s;
        return all.Count == 0 ? ObservationStatus.ELIGIBLE : ObservationStatus.INVALID;
    }
    public static DatasetScopeDecision Reduce(TrendResolutionRequest old, TrendResolutionRequest next, string reason, string[] gaps, string reviewer, DateTimeOffset at)
    {
        Validate(old); Validate(next); var a = old.Input.Request; var b = next.Input.Request;
        var allowed = new[] { "AUTHORITY_UNAVAILABLE", "HISTORICAL_COVERAGE_UNAVAILABLE", "IDENTITY_UNRESOLVED" };
        var oldMembers = old.Input.Closure.Dataset.Source.Dataset.Universes.SelectMany(u => u.Members).ToHashSet();
        var newMembers = next.Input.Closure.Dataset.Source.Dataset.Universes.SelectMany(u => u.Members).ToHashSet();
        if (!allowed.Contains(reason) || gaps.Length == 0 || gaps.Any(string.IsNullOrWhiteSpace) || string.IsNullOrWhiteSpace(reviewer) ||
            old.RequestId == next.RequestId || at < a.RequestedAt || at < b.RequestedAt ||
            b.CoverageStart < a.CoverageStart || b.CoverageEnd > a.CoverageEnd || b.CoverageStart > b.CoverageEnd ||
            b.CandidateVersion != a.CandidateVersion || b.ParameterSetId != a.ParameterSetId || b.PriceBasisVersion != a.PriceBasisVersion ||
            old.ReplaySemantics != next.ReplaySemantics || !newMembers.IsSubsetOf(oldMembers) ||
            (b.CoverageStart == a.CoverageStart && b.CoverageEnd == a.CoverageEnd && oldMembers.SetEquals(newMembers)))
            throw new ArgumentException("Explicit evidence-based narrower NEW request required; no outcomes or parameter changes allowed.");
        var decision = new DatasetScopeDecision(old.RequestId, next.RequestId, old.RequestDigest, next.RequestDigest,
            $"{a.CoverageStart:yyyy-MM-dd}..{a.CoverageEnd:yyyy-MM-dd} -> {b.CoverageStart:yyyy-MM-dd}..{b.CoverageEnd:yyyy-MM-dd}",
            $"{a.UniverseDefinitionVersion} -> {b.UniverseDefinitionVersion}; removed={oldMembers.Except(newMembers).Count()}", reason, gaps, reviewer, at, "");
        return decision with { Digest = ResearchMath.Digest(decision) };
    }
}
