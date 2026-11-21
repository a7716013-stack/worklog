using WorkJournal.Web.Models.HistoricalResearch.Components;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

// Supplements existing permanent identity/universe contracts with dated classification and trading state.
public sealed record InstrumentContextVersion(Guid InstrumentId,string Kind,string Value,DateTimeOffset EffectiveFrom,DateTimeOffset? EffectiveTo,
    DateTimeOffset? KnownAt,Guid EvidenceId,string SourceVersion,string Digest);
public sealed record PilotMarketContext(HistoricalInstrumentIdentityRecord[] Identities,HistoricalUniverseSnapshot[] Universes,
    HistoricalUniverseMember[] Members,HistoricalTradingSession[] Sessions,HistoricalCorporateAction[] Actions,InstrumentContextVersion[] Versions,string[] ReviewedDigests);
public sealed record PilotContextAssessment(Guid? InstrumentId,bool? HistoricalMember,bool? Tradable,string? Industry,string CalendarState,
    string CorporateActionState,string[] ActionDigests,string[] BlockingReasons,string Digest);
public static class PilotMarketContextRules
{
    public static PilotContextAssessment Assess(PilotMarketContext c,string market,string symbol,DateOnly date,DateTimeOffset cutoff)
    {
        var gaps=new List<string>();bool Trusted<T>(T x)=>c.ReviewedDigests.Contains(ResearchMath.Digest(x));
        var instant=new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue),TimeSpan.FromHours(8));
        var identities=c.Identities.Where(Trusted).Where(i=>(i.ListingDate is null||i.ListingDate<=date)&&(i.DelistingDate is null||date<i.DelistingDate)).ToArray();
        var id=ComponentEvidence.ResolveInstrument(identities,market,symbol,instant,cutoff);
        if(id is null)gaps.Add("HISTORICAL_IDENTITY_UNVERIFIED");
        var universes=c.Universes.Where(u=>u.Market==market&&u.TradeDate==date&&u.Digest==ResearchMath.Digest(u with{Digest=""})&&Trusted(u)).ToArray();
        bool? member=null;
        if(id is not null&&universes.Length==1){var u=universes[0];var members=c.Members.Where(m=>m.UniverseSnapshotId==u.UniverseSnapshotId&&m.InstrumentId==id&&Trusted(m)).ToArray();
            if(members.Length==1&&members[0].ListedAtDate is not null&&c.Versions.Any(v=>v.InstrumentId==id&&v.Kind=="UNIVERSE_MEMBERSHIP"&&v.Value==u.UniverseSnapshotId.ToString()&&v.SourceVersion==u.Digest&&v.KnownAt<=cutoff&&v.KnownAt is not null&&v.EffectiveFrom<=instant&&(v.EffectiveTo is null||instant<v.EffectiveTo)&&v.EvidenceId!=Guid.Empty&&v.Digest==ResearchMath.Digest(v with{Digest=""})&&Trusted(v)))member=u.InstrumentIds.Contains(id.Value)&&members[0].ListedAtDate==true;}
        if(member is null)gaps.Add("HISTORICAL_UNIVERSE_UNVERIFIED");
        var sessions=c.Sessions.Where(s=>s.Market==market&&s.TradeDate==date&&Trusted(s)&&s.EffectiveAt<=instant).ToArray();
        var coverage=ComponentEvidence.Calendar(sessions,date,date,cutoff);
        var state=coverage.UnknownSessions==0&&sessions.Length==1?sessions[0].SessionType.ToString():"UNKNOWN";
        if(state=="UNKNOWN")gaps.Add("TRADING_CALENDAR_UNVERIFIED");
        var versions=c.Versions.Where(v=>v.InstrumentId==id&&v.KnownAt is not null&&v.KnownAt<=cutoff&&v.EffectiveFrom<=instant&&
            (v.EffectiveTo is null||instant<v.EffectiveTo)&&v.EvidenceId!=Guid.Empty&&PilotScopeRules.Digest(v.SourceVersion)&&v.Digest==ResearchMath.Digest(v with{Digest=""})&&Trusted(v)).ToArray();
        var industries=versions.Where(v=>v.Kind=="INDUSTRY").ToArray();var trading=versions.Where(v=>v.Kind=="TRADING_STATE"&&v.Value is "TRADING" or "SUSPENDED").ToArray();
        string? industry=industries.Length==1?industries[0].Value:null;if(industry is null)gaps.Add("HISTORICAL_INDUSTRY_UNVERIFIED");
        bool? tradable=state is "ScheduledHoliday" or "EmergencyClosure"?false:state!="UNKNOWN"&&trading.Length==1&&member==true?trading[0].Value=="TRADING":null;
        if(tradable is null)gaps.Add("INSTRUMENT_SUSPENSION_HISTORY_UNVERIFIED");
        // No adjustment arithmetic: raw facts are never overwritten. Known-at and source revision bind every visible action.
        var actions=ComponentEvidence.VisibleActions(c.Actions,cutoff).Where(a=>a.InstrumentId==id&&a.SourceEvidenceId!=Guid.Empty&&
            !string.IsNullOrWhiteSpace(a.RevisionId)&&(a.AdjustmentFactor is null||a.AdjustmentFactor>0)&&Trusted(a)&&a.ActionType is "CashDividend" or "StockDividend" or "CapitalReduction" or "StockSplit" or "PriceBasisChange").ToArray();
        var unverified=c.Actions.Where(a=>a.InstrumentId==id&&(a.EffectiveDate??a.ExDate)<=date).Any(a=>!actions.Contains(a));
        var actionState=unverified?"UNVERIFIED_OR_FUTURE_KNOWN":actions.Length==0?"NO_VERIFIED_ACTION_COVERAGE":"VERIFIED_VISIBLE_ACTIONS_ONLY";
        if(actionState!="VERIFIED_VISIBLE_ACTIONS_ONLY")gaps.Add("CORPORATE_ACTION_COVERAGE_REQUIRED");
        var result=new PilotContextAssessment(id,member,tradable,industry,state,actionState,actions.Select(ResearchMath.Digest).Order().ToArray(),gaps.ToArray(),"");return result with{Digest=ResearchMath.Digest(result)};
    }
}
