using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
namespace WorkJournal.Api.Services;
public sealed class Phase4PReadAdapter(Phase4OReadAdapter parent)
{
    public Phase4PReport Read()
    {
        try{var report=HistoricalDatasetStore.Read<Phase4PReport>(Path.Combine(AppContext.BaseDirectory,"Research","phase4p-state.json"));Phase4POperations.Validate(report,parent.Read());return report;}
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or NullReferenceException){throw new ApiFault(503,"DATA_NOT_READY");}
    }
}
