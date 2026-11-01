using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.HistoricalResearch.Components;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

public sealed class ComponentDatasetGate(string evidenceRoot,string authorityRoot,string closureRoot)
{
    public ComponentDatasetEvaluation Evaluate(ComponentDatasetInput input)
    {
        var request=input.Request;var submission=input.Closure.Dataset;var d=submission.Source.Dataset;
        var manifest=ComponentEvidence.Dependencies(request.Component,request.ParameterSetId,request.RequestedAt);
        var gaps=new List<string>();void Gap(bool condition,string reason){if(condition)gaps.Add(reason);}
        Gap(input.DataMode!=ComponentDataMode.RealApproved||d.Archives.Any(a=>a.Synthetic||!Uri.TryCreate(a.SourceUri,UriKind.Absolute,out var u)||u.Scheme!="https"),"REAL_SOURCE_REQUIRED");
        Gap(request.RequestDigest!=ResearchMath.Digest(request with{RequestDigest=""}),"REQUEST_DIGEST_MISMATCH");
        Gap(request.CandidateVersion!=V3CandidateCalculator.Version||request.ParameterSetId!=d.ParameterSetId||
            request.PriceBasisVersion!=d.PricePolicy.PriceBasisVersion||string.IsNullOrWhiteSpace(request.RequestedBy)||
            request.RequestedAt<submission.Review?.ReviewedAt||request.RequestedAt<d.CreatedAt,"REQUEST_IDENTITY_MISMATCH");
        Gap(submission.RequestedComponents.Length!=1||submission.RequestedComponents[0]!=request.Component,"COMPONENT_SCOPE_MISMATCH");
        Gap(request.CoverageStart!=d.Dates.Min(x=>x.TradeDate)||request.CoverageEnd!=d.Dates.Max(x=>x.TradeDate),"COVERAGE_SCOPE_MISMATCH");
        Gap(!request.RequiredAuthorityCaseIds.Order().SequenceEqual(input.Closure.Closures.Select(c=>c.CaseId).Order()),"CASE_ID_SCOPE_MISMATCH");
        Gap(!request.RequiredPolicyIds.Order().SequenceEqual(d.Policies.Where(p=>p.DatasetType==HistoricalDatasetType.Price).Select(p=>p.PolicyId).Order()),"POLICY_SCOPE_MISMATCH");
        var closure=new AuthorityClosureStore(closureRoot,evidenceRoot).Evaluate(input.Closure);
        Gap(closure.Status!="CLOSURE_READY_REQUIRES_DATASET_APPROVAL","BLOCKED_BY_AUTHORITY");
        var authority=new RealDataApproval(evidenceRoot,authorityRoot).Evaluate(submission);
        Gap(!authority.ComponentApprovals.Any(r=>r.EvidenceCode==request.Component),"PHASE4DE_APPROVAL_REQUIRED");
        var build=new HistoricalDatasetBuilder().Build(d);
        var start=d.Calendar.Sessions.IsEmpty?request.CoverageStart:d.Calendar.Sessions.Min();
        var end=d.Calendar.Sessions.IsEmpty?request.CoverageEnd:d.Calendar.Sessions.Max();
        var cutoff=d.Dates.Min(x=>x.DataCutoff);
        var calendar=ComponentEvidence.Calendar(input.Sessions,start,end,cutoff);
        Gap(calendar.UnknownSessions>0||calendar.ExpectedSessions is null,"CALENDAR_COVERAGE_UNKNOWN");
        Gap(!input.Sessions.Where(s=>s.IsTradingDay==true&&s.TradeDate>=start&&s.TradeDate<=end).Select(s=>s.TradeDate).Order().SequenceEqual(d.Calendar.Sessions.Order()),"CALENDAR_SESSION_MISMATCH");
        bool EvidenceIn(ClosureDomain domain,Guid id)=>closure.Cases.Any(c=>c.Case.Domain==domain&&c.Case.EvidenceIds.Contains(id));
        Gap(input.Sessions.Any(s=>s.PolicyVersion!=d.Calendar.Version||!EvidenceIn(ClosureDomain.TradingCalendar,s.SourceEvidenceId)),"CALENDAR_EVIDENCE_BINDING");
        Gap(input.Identities.Any(i=>!EvidenceIn(ClosureDomain.HistoricalInstrumentIdentity,i.EvidenceId)),"IDENTITY_EVIDENCE_BINDING");
        var universeUnknown=0;var universeVerified=0;int? expectedMembers=0;
        foreach(var day in d.Dates)
        {
            var matching=input.Universes.Where(u=>u.TradeDate==day.TradeDate).ToArray();
            var old=d.Universes.Where(u=>u.AsOfDate==day.TradeDate).ToArray();
            if(matching.Length!=1||old.Length!=1){universeUnknown++;expectedMembers=null;continue;}
            var u=matching[0];var members=input.Members.Where(m=>m.UniverseSnapshotId==u.UniverseSnapshotId).ToArray();
            bool valid=u.Digest==ResearchMath.Digest(u with{Digest=""})&&u.UniverseSnapshotId==old[0].SnapshotId&&u.EligibilityDefinitionVersion==request.UniverseDefinitionVersion&&
                u.InstrumentIds.Order().SequenceEqual(old[0].Members.Order())&&u.InstrumentIds.Distinct().Count()==u.InstrumentIds.Length&&
                u.SourceEvidenceIds.Length>0&&u.SourceEvidenceIds.All(id=>EvidenceIn(ClosureDomain.HistoricalUniverse,id))&&
                u.AuthorityClosureIds.Contains(input.Closure.Closures.SingleOrDefault(c=>c.Domain==ClosureDomain.HistoricalUniverse)?.CaseId??Guid.Empty)&&
                members.Select(m=>m.InstrumentId).Order().SequenceEqual(u.InstrumentIds.Order())&&
                members.All(m=>new[]{"Active","Suspended","Delisted","Transferred"}.Contains(m.MembershipStatus)&&m.ListedAtDate.HasValue&&m.TradableAtDate.HasValue&&
                    !string.IsNullOrWhiteSpace(m.EligibilityReason)&&u.SourceEvidenceIds.Contains(m.SourceEvidenceId));
            if(!valid){universeUnknown++;expectedMembers=null;}else{universeVerified+=members.Length;if(expectedMembers.HasValue)expectedMembers+=members.Length;}
        }
        Gap(universeUnknown>0||input.Universes.Length!=d.Dates.Length,"SURVIVORSHIP_BIAS_RISK");
        bool reviewed(string digest,Guid[] ids,string claim,DateOnly date)
        {
            try{var r=new EvidenceAcquisitionStore(evidenceRoot).ReadReview(digest);return ids.Contains(r.EvidenceId)&&r.EffectiveFrom<=date&&r.EffectiveTo>=date&&
                r.ReviewedAt<=request.RequestedAt&&r.Supported.Contains(claim)&&!r.Unsupported.Contains(claim)&&!r.Ambiguous.Contains(claim);}
            catch(Exception e) when(e is IOException or ArgumentException or InvalidDataException or InvalidOperationException){return false;}
        }
        int unknownAvailability=0,revisionUnknown=0,future=0,verified=0;
        var selected=build.Decisions.Where(x=>x.Eligible).Select(x=>x.FactChecksum).ToHashSet();
        var prices=d.Facts.Where(f=>f.DatasetType==HistoricalDatasetType.Price&&selected.Contains(f.FactChecksum)).ToArray();
        unknownAvailability=d.Facts.Count(f=>f.DatasetType==HistoricalDatasetType.Price&&!selected.Contains(f.FactChecksum)&&HistoricalAvailability.Resolve(f,d.Policies.SingleOrDefault(p=>p.PolicyId==f.AvailabilityPolicyId)).At is null);
        revisionUnknown=d.Facts.Count(f=>f.DatasetType==HistoricalDatasetType.Price&&!selected.Contains(f.FactChecksum)&&!input.RevisionPolicies.Any(p=>p.Provider==f.Provider&&p.DatasetType==f.DatasetType&&p.CanRevise.HasValue&&p.ReviewStatus==ClaimSupport.SUPPORTED));
        future=build.Decisions.Where(x=>x.Reason=="AVAILABILITY_GAP:FUTURE_RECORD_OR_REVISION").Select(x=>x.FactChecksum).Distinct().Count();
        var parsed=new Dictionary<Guid,ParsedPriceRow[]>();
        foreach(var f in prices)
        {
            var claims=input.AvailabilityClaims.Where(a=>a.FactId==f.FactChecksum).ToArray();var claim=claims.Length==1?claims[0]:null;
            var status=ComponentEvidence.Availability(f,claim,submission.Source.EvaluatedAt);
            var resolved=HistoricalAvailability.Resolve(f,d.Policies.SingleOrDefault(p=>p.PolicyId==f.AvailabilityPolicyId)).At;
            bool availability=claim is not null&&status==ComponentAvailability.Available&&resolved==(claim.RevisionAvailableAt??claim.OriginalAvailableAt)&&
                d.Policies.Any(p=>p.PolicyId==f.AvailabilityPolicyId&&p.Version==claim.PolicyVersion)&&
                reviewed(claim.ReviewDigest,claim.EvidenceIds,"HistoricalPublication",f.TradeDate);
            if(status==ComponentAvailability.FutureKnown)future++;
            else if(!availability)unknownAvailability++;
            var policy=input.RevisionPolicies.Where(p=>p.Provider==f.Provider&&p.DatasetType==f.DatasetType&&p.EffectiveFrom<=f.TradeDate&&(!p.EffectiveTo.HasValue||f.TradeDate<p.EffectiveTo)).ToArray();
            bool revision=policy.Length==1&&policy[0].CanRevise.HasValue&&policy[0].ReviewStatus==ClaimSupport.SUPPORTED&&policy[0].FrozenAt.HasValue&&policy[0].FrozenAt<=request.RequestedAt&&
                !string.IsNullOrWhiteSpace(policy[0].RevisionIdentifierMethod)&&!string.IsNullOrWhiteSpace(policy[0].OriginalPublicationRule)&&
                (policy[0].CanRevise==false||!string.IsNullOrWhiteSpace(policy[0].CorrectionPublicationRule))&&reviewed(policy[0].ReviewDigest,policy[0].EvidenceIds,"RevisionBehavior",f.TradeDate);
            if(!revision)revisionUnknown++;
            var lineage=input.Lineage.Where(l=>l.FactId==f.FactChecksum).ToArray();bool rowValid=false;
            if(lineage.Length==1)
            {
                var l=lineage[0];
                try
                {
                    if(!parsed.ContainsKey(l.RawEvidenceId))parsed[l.RawEvidenceId]=ComponentEvidence.ParsePrice(evidenceRoot,l.RawEvidenceId);
                    var row=parsed[l.RawEvidenceId].SingleOrDefault(p=>p.Locator==l.RawRecordLocator);
                    var identity=input.Identities.Where(i=>i.InstrumentId==l.InstrumentId&&i.Symbol==l.RawRecordLocator.Symbol).ToArray();
                    rowValid=row is not null&&row.ParsedValueDigest==l.ParsedValueDigest&&row.Value==f.Value&&l.ParserVersion==ComponentEvidence.ParserVersion&&
                        l.RawRecordLocator.ResponseDigest==f.SourceChecksum&&l.RawRecordLocator.Symbol==f.SourceInstrumentKey&&l.TradeDate==f.TradeDate&&
                        l.DatasetType==f.DatasetType&&l.DatasetSnapshotId==build.DatasetSnapshotId&&l.AvailableAtPolicyId==f.AvailabilityPolicyId&&
                        ComponentEvidence.ProviderRevision(l.RevisionIdentity,f.SourceChecksum)&&l.RevisionIdentity==f.RevisionId&&
                        identity.Length>0&&ComponentEvidence.ResolveInstrument(identity,identity[0].Market,f.SourceInstrumentKey,f.EffectiveAt,resolved??cutoff)==l.InstrumentId&&
                        EvidenceIn(ClosureDomain.HistoricalPriceVolume,l.RawEvidenceId);
                }
                catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException){rowValid=false;}
            }
            if(!rowValid)gaps.Add("RAW_FACT_LINEAGE_OR_REVISION_IDENTITY_GAP");
            if(availability&&revision&&rowValid)verified++;
        }
        Gap(unknownAvailability>0,"UNKNOWN_AVAILABILITY");Gap(revisionUnknown>0,"REVISION_UNKNOWN");
        Gap(prices.Length==0,"PRICE_FACTS_MISSING");
        // No latest-revision shortcut. Builder must select unique PIT-eligible revisions; conflicting same-time facts are blocked.
        Gap(prices.GroupBy(f=>new{f.SourceInstrumentKey,f.TradeDate,f.AvailableAt}).Any(g=>g.Count()>1),"DUPLICATE_OR_CONFLICTING_REVISION");
        var basis=ComponentEvidence.PriceBasis(d.PricePolicy.Basis,d.PricePolicy.Basis,d.PricePolicy.CorporateActionPolicyVersion,d.PricePolicy.Frozen)&&prices.All(f=>f.PriceBasisVersion==request.PriceBasisVersion);
        Gap(!basis,"PRICE_BASIS_INCONSISTENT");
        Gap(ComponentEvidence.VisibleActions(input.CorporateActions,cutoff).Any(a=>!EvidenceIn(ClosureDomain.CorporateActionPriceBasis,a.SourceEvidenceId)),"CORPORATE_ACTION_UNVERIFIABLE");
        // Required declared scope must be complete, not an invented percentage/stock-count threshold.
        var coverage=new ComponentDatasetCoverageReport(calendar.ExpectedSessions,calendar.VerifiedSessions,calendar.UnknownSessions,expectedMembers,
            universeVerified,expectedMembers.HasValue?Math.Max(0,expectedMembers.Value-universeVerified):null,expectedMembers.HasValue&&calendar.ExpectedSessions.HasValue?build.Quality.Coverage.Where(c=>c.Domain.Contains("Price",StringComparison.OrdinalIgnoreCase)).Sum(c=>c.Expected):null,
            verified,future,unknownAvailability,revisionUnknown,universeUnknown>0,basis,"");
        coverage=coverage with{Digest=ResearchMath.Digest(coverage)};
        var result=new ComponentDatasetEvaluation(gaps.Count==0?"READY_FOR_EXPLICIT_APPROVAL":"BLOCKED_BY_AUTHORITY_OR_DATA",input.DataMode,manifest,coverage,
            build.DatasetSnapshotId,build.DatasetChecksum,gaps.Distinct().Order(StringComparer.Ordinal).ToArray(),ResearchMath.Digest(input),"");
        return result with{Digest=ResearchMath.Digest(result)};
    }
}
