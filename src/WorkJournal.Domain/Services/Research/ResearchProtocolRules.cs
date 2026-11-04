using System.Collections.Immutable;
using WorkJournal.Web.Models.Research;
namespace WorkJournal.Web.Services.Research;

public static class ResearchProtocolRules
{
    public static ResearchPeriod Period(ResearchFold f,ResearchPartition p)=>p switch {ResearchPartition.Train=>f.Train,ResearchPartition.Validation=>f.Validation,_=>f.Oos};
    public static void Validate(ResearchProtocol p,ImmutableArray<DateOnly> sessions)
    {
        if(string.IsNullOrWhiteSpace(p.Version)||p.Folds.IsDefaultOrEmpty||p.OutcomeHorizons.IsDefaultOrEmpty||p.Quantiles<2||p.EmbargoSessions<0 ||
            p.OutcomeHorizons.Any(n=>n<=0)||p.OutcomeHorizons.Distinct().Count()!=p.OutcomeHorizons.Length ||
            p.Folds.Select(f=>f.Code).Distinct().Count()!=p.Folds.Length||p.Folds.Any(f=>string.IsNullOrWhiteSpace(f.Code)))throw new ArgumentException("Explicit valid research protocol required.");
        if(p.PurgeRule!="LabelEndWithinPartition-v1"||p.QuantileRule!="MidrankKeepTies-v1"||p.DistributionRule!="LinearOrderStatistic-v1"||p.AblationRule!="SubtractNoRedistribution-v1")
            throw new ArgumentException("Unsupported research methodology version.");
        if(sessions.IsDefaultOrEmpty || !sessions.SequenceEqual(sessions.Distinct().Order()))throw new ArgumentException("Ordered unique historical outcome calendar required.");
        var folds=p.Folds.OrderBy(f=>f.Oos.Start).ToArray();
        foreach(var f in folds)
        {
            foreach(var window in new[]{f.Train,f.Validation,f.Oos})
                if(window.Start>window.End || !sessions.Contains(window.Start)||!sessions.Contains(window.End))throw new ArgumentException("Invalid research period/calendar.");
            if(f.Train.End>=f.Validation.Start || f.Validation.End>=f.Oos.Start)throw new ArgumentException("Chronological partitions overlap.");
            if(sessions.Count(d=>d>f.Train.End&&d<f.Validation.Start)<p.EmbargoSessions || sessions.Count(d=>d>f.Validation.End&&d<f.Oos.Start)<p.EmbargoSessions)
                throw new ArgumentException("Insufficient embargo sessions.");
        }
        if(folds.Zip(folds.Skip(1)).Any(pair=>pair.First.Oos.End>=pair.Second.Oos.Start))throw new ArgumentException("Walk-forward OOS windows must not overlap.");
    }
    public static ResearchRunStatus Transition(ResearchRunStatus current,ResearchRunStatus next,bool finalized)
    {
        if(finalized)throw new InvalidOperationException("Finalized research is immutable.");
        var allowed=(current,next) switch {
            (ResearchRunStatus.Created,ResearchRunStatus.Prepared)=>true,
            (ResearchRunStatus.Prepared,ResearchRunStatus.Running)=>true,
            (ResearchRunStatus.Running,ResearchRunStatus.Completed)=>true,
            (_,ResearchRunStatus.Failed or ResearchRunStatus.Rejected) when current is ResearchRunStatus.Created or ResearchRunStatus.Prepared or ResearchRunStatus.Running=>true,
            _=>false};
        if(!allowed)throw new InvalidOperationException("Invalid lifecycle transition; automatic promotion is prohibited.");
        return next;
    }
    public static SelectionReceipt Select(DevelopmentScorecard card,DateTimeOffset selectedAt,string rationale)
    {
        if(card.Train.Any(x=>x.Partition!=ResearchPartition.Train)||card.Validation.Any(x=>x.Partition!=ResearchPartition.Validation)||
            card.Train.IsEmpty||card.Validation.IsEmpty||card.Validation.All(x=>x.OutcomePairCount==0))
            throw new ArgumentException("Selection requires train/validation evidence and cannot use OOS.");
        if(card.Digest!=ResearchMath.Digest(card with {Digest=""})||selectedAt<card.OutcomesKnownAt||string.IsNullOrWhiteSpace(rationale))
            throw new ArgumentException("Invalid selection provenance/time/rationale.");
        return new(card.ResearchRunId,card.Fold,card.ParameterSetId,card.ParameterChecksum,card.CandidateVersion,card.Digest,selectedAt,rationale);
    }
    public static ImmutableArray<DevelopmentScorecard> Compare(IEnumerable<DevelopmentScorecard> cards)
    {
        var array=cards.OrderBy(c=>c.Fold,StringComparer.Ordinal).ThenBy(c=>c.ParameterSetId).ToImmutableArray();
        if(array.IsEmpty || array.GroupBy(c=>(c.Fold,c.ParameterSetId)).Any(g=>g.Count()>1))throw new ArgumentException("Explicit unique parameter candidates required.");
        foreach(var c in array)
            if(c.Digest!=ResearchMath.Digest(c with {Digest=""})||c.Train.Any(m=>m.Partition!=ResearchPartition.Train)||c.Validation.Any(m=>m.Partition!=ResearchPartition.Validation))throw new ArgumentException("OOS/modified scorecard rejected.");
        return array; // Factual comparison only; no invented composite score or automatic winner.
    }
}
