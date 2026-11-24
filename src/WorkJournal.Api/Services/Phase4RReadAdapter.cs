using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
namespace WorkJournal.Api.Services;
public sealed class Phase4RReadAdapter(Phase4QReadAdapter parent)
{
    public Phase4RReport Read()
    {
        try{var r=HistoricalDatasetStore.Read<Phase4RReport>(Path.Combine(AppContext.BaseDirectory,"Research","phase4r-state.json"));Phase4ROperations.Validate(r,parent.Read());return r;}
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or NullReferenceException){throw new ApiFault(503,"DATA_NOT_READY");}
    }
}
