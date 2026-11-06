using System.Collections.Immutable;
using WorkJournal.Application.Research;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;
public static class Phase4ISampleChecks
{
    // Explicit simulated policy unit fixtures, never exported to authority/evidence stores.
    public static void Run(BaselineResearchReport original,ResearchProtocol protocol,Action<bool,string> check)
    {
        var insufficient=Phase4IProjection.Assess(original,protocol,SamplePolicy.Phase4I);
        check(insufficient.Status=="INSUFFICIENT_SAMPLE"&&insufficient.Metrics is null&&insufficient.IC is null&&insufficient.Stability is null,"small approved fixture exposes no fake performance");
        var source=original.Metrics.First(m=>m.Variable=="Trend");
        var metrics=(from fold in protocol.Folds from part in new[]{ResearchPartition.Train,ResearchPartition.Validation} from horizon in protocol.OutcomeHorizons
            select source with{Fold=fold.Code,Partition=part,Horizon=horizon,OutcomePairCount=200,
                DailyIc=Enumerable.Range(0,20).Select(i=>new DailyIc(new DateOnly(2025,1,1).AddDays(i),Math.Max(10,protocol.Quantiles),0.1m,0.2m)).ToImmutableArray(),
                Quantiles=Enumerable.Range(1,protocol.Quantiles).Select(i=>new QuantileResult(i,20,0.1m,0.1m,0.5m)).ToImmutableArray()}).ToArray();
        var complete=original with{Run=original.Run with{Status="BASELINE_COMPLETED"},Metrics=metrics};
        check(Phase4IProjection.Assess(complete,protocol,SamplePolicy.Phase4I).Status=="COMPLETED","sample policy exact inclusive boundary");
        var oneBelow=complete with{Metrics=metrics.Select((m,i)=>i==0?m with{OutcomePairCount=199}:m).ToArray()};
        check(Phase4IProjection.Assess(oneBelow,protocol,SamplePolicy.Phase4I).Status=="INSUFFICIENT_SAMPLE","one partition/horizon below threshold blocks whole summary");
        var duplicateDates=complete with{Metrics=metrics.Select(m=>m with{DailyIc=Enumerable.Repeat(m.DailyIc[0],20).ToImmutableArray()}).ToArray()};
        check(Phase4IProjection.Assess(duplicateDates,protocol,SamplePolicy.Phase4I).Status=="INSUFFICIENT_SAMPLE","duplicate dates cannot inflate sample eligibility");
        var constant=complete with{Metrics=metrics.Select(m=>m with{DailyIc=m.DailyIc.Select(d=>d with{PearsonIc=null,RankIc=null}).ToImmutableArray()}).ToArray()};
        check(Phase4IProjection.Assess(constant,protocol,SamplePolicy.Phase4I).Metrics is null,"constant cross section cannot produce invented IC");
        var emptyQuantile=complete with{Metrics=metrics.Select(m=>m with{Quantiles=m.Quantiles.Select((q,i)=>i==0?q with{Count=0}:q).ToImmutableArray()}).ToArray()};
        check(Phase4IProjection.Assess(emptyQuantile,protocol,SamplePolicy.Phase4I).Status=="INSUFFICIENT_SAMPLE","empty quantile rejects apparently large sample");
    }
}
