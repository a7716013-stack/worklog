using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

public static class HistoricalAvailability
{
    public static string FactChecksum(HistoricalFact f)=>ResearchMath.Digest(f with{FactChecksum=""});
    public static string UniverseChecksum(HistoricalUniverse u)=>ResearchMath.Digest(u with{Members=u.Members.Order().ToImmutableArraySafe(),Checksum=""});
    private static System.Collections.Immutable.ImmutableArray<T> ToImmutableArraySafe<T>(this IEnumerable<T> values)=>System.Collections.Immutable.ImmutableArray.CreateRange(values);
    public static (DateTimeOffset? At,string Reason) Resolve(HistoricalFact f,FrozenAvailabilityPolicy? policy)
    {
        if(f.Confidence==AvailabilityConfidence.Unknown)return (null,"AVAILABILITY_GAP:UNKNOWN");
        if(!Enum.IsDefined(f.Confidence)||policy is null||policy.Status!=FoundationStatus.Finalized||policy.FrozenAt is null||
            policy.FrozenAt<policy.CreatedAt||policy.PolicyId!=f.AvailabilityPolicyId||policy.Provider!=f.Provider||policy.DatasetType!=f.DatasetType||
            f.TradeDate<policy.EffectiveFrom||(policy.EffectiveTo.HasValue&&f.TradeDate>policy.EffectiveTo)||
            new[]{policy.Version,policy.EvidenceSource,policy.EvidenceNote,policy.ReviewReference,policy.Timezone,f.AvailabilityEvidence,f.RevisionEvidence}.Any(string.IsNullOrWhiteSpace))
            return (null,"AVAILABILITY_GAP:UNVERIFIED_POLICY_OR_REVISION");
        TimeZoneInfo zone;
        try{zone=TimeZoneInfo.FindSystemTimeZoneById(policy.Timezone);}catch(TimeZoneNotFoundException){return(null,"AVAILABILITY_GAP:TIMEZONE");}
        DateTimeOffset? at=null;
        if(f.Confidence==AvailabilityConfidence.Known&&policy.Rule==AvailabilityRule.SourceTimestamp)at=f.AvailableAt;
        if(f.Confidence==AvailabilityConfidence.DerivedFromVerifiedRule&&policy.Rule==AvailabilityRule.VerifiedLocalPublicationTime&&policy.LocalPublicationTime.HasValue)
        {
            var local=f.TradeDate.ToDateTime(policy.LocalPublicationTime.Value,DateTimeKind.Unspecified);
            if(zone.IsInvalidTime(local)||zone.IsAmbiguousTime(local))return(null,"AVAILABILITY_GAP:AMBIGUOUS_LOCAL_TIME");
            at=new DateTimeOffset(local,zone.GetUtcOffset(local)).ToUniversalTime();
            if(f.AvailableAt.HasValue&&f.AvailableAt!=at)return(null,"AVAILABILITY_GAP:RULE_TIMESTAMP_CONFLICT");
        }
        if(at is null||at<f.EffectiveAt||at>f.ObservedAt||f.ObservedAt>f.RetrievedAt)return(null,"AVAILABILITY_GAP:TIMESTAMP_ORDER");
        return(at,"AVAILABLE_WITH_REVIEWED_SOURCE_EVIDENCE");
    }
}
