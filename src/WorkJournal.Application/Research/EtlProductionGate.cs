namespace WorkJournal.Application.Research;
public sealed record EtlDispatchResult(string Mode,string Status,bool Executed);
public static class EtlProductionGate
{
    // Only invoked by an explicit controlled operator; never registered as a hosted service.
    public static async Task<EtlDispatchResult> Run(string root,string id,string mode,ReadinessCheck[] checks,CancellationToken token=default)
    {
        var readiness=Phase4MOperations.Readiness(mode,checks);
        if(mode=="Disabled")return new(mode,"DISABLED",false);
        if(mode=="Dry-run")return new(mode,"DRY_RUN_NO_IO",false);
        if(!readiness.CanDispatch)throw new InvalidOperationException("DEPLOYMENT_READINESS_BLOCKED");
        AcquisitionSafety.RequireCurrentSources(root);
        var receipt=await new EtlScheduler(root).RunOnce(id,token);
        return new(mode,receipt?.Status??"NO_DISPATCH",receipt is not null);
    }
}
