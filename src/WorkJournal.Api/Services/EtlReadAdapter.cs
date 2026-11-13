using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
namespace WorkJournal.Api.Services;
public sealed class EtlReadAdapter
{
    public EtlOperationsSnapshot Operations()
    {
        try{var r=HistoricalDatasetStore.Read<EtlOperationsSnapshot>(Path.Combine(AppContext.BaseDirectory,"Research","etl-operations.json"));Phase4LOperations.Validate(r);return r;}
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {throw new ApiFault(503,"DATA_NOT_READY");}
    }
    public EtlPublication Read()
    {
        try{var r=HistoricalDatasetStore.Read<EtlPublication>(Path.Combine(AppContext.BaseDirectory,"Research","etl-state.json"));EtlPipeline.ValidatePublication(r);return r;}
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {throw new ApiFault(503,"DATA_NOT_READY");}
    }
    public EtlJobReport Job(string id)=>Read().Jobs.SingleOrDefault(j=>j.JobId==id)??throw new ApiFault(404,"NOT_FOUND");
}
