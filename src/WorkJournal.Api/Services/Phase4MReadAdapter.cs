using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
namespace WorkJournal.Api.Services;
public sealed class Phase4MReadAdapter(Phase4IReadAdapter parent,Phase4JReadAdapter evidence)
{
    public Phase4MReport Read()
    {
        try {var r=HistoricalDatasetStore.Read<Phase4MReport>(Path.Combine(AppContext.BaseDirectory,"Research","phase4m-state.json"));Phase4MOperations.Validate(r,parent.Read(),evidence.Read());return r;}
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException){throw new ApiFault(503,"DATA_NOT_READY");}
    }
}
