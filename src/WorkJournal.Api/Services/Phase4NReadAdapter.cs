using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
namespace WorkJournal.Api.Services;
public sealed class Phase4NReadAdapter(Phase4IReadAdapter parent,Phase4JReadAdapter evidence,Phase4MReadAdapter workbench)
{
    public Phase4NReport Read()
    {
        try {var r=HistoricalDatasetStore.Read<Phase4NReport>(Path.Combine(AppContext.BaseDirectory,"Research","phase4n-state.json"));Phase4NOperations.Validate(r,parent.Read(),evidence.Read(),workbench.Read());return r;}
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or NullReferenceException){throw new ApiFault(503,"DATA_NOT_READY");}
    }
}
