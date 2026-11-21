using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
namespace WorkJournal.Api.Services;
public sealed class Phase4QReadAdapter(Phase4PReadAdapter parent,Phase4IReadAdapter historical)
{
    public Phase4QReport Read()
    {
        try{var r=HistoricalDatasetStore.Read<Phase4QReport>(Path.Combine(AppContext.BaseDirectory,"Research","phase4q-state.json"));Phase4QOperations.Validate(r,parent.Read(),historical.Read());return r;}
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or NullReferenceException){throw new ApiFault(503,"DATA_NOT_READY");}
    }
}
