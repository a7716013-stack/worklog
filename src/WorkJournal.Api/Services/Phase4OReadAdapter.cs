using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
namespace WorkJournal.Api.Services;
public sealed class Phase4OReadAdapter(Phase4NReadAdapter parent)
{
    public Phase4OReport Read()
    {
        try{var r=HistoricalDatasetStore.Read<Phase4OReport>(Path.Combine(AppContext.BaseDirectory,"Research","phase4o-state.json"));Phase4OOperations.Validate(r,parent.Read());return r;}
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or NullReferenceException){throw new ApiFault(503,"DATA_NOT_READY");}
    }
}
