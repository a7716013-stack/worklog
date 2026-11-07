using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
namespace WorkJournal.Api.Services;
public sealed class Phase4JReadAdapter(Phase4IReadAdapter parent)
{
    public Phase4JReport Read()
    {
        try
        {
            var r=HistoricalDatasetStore.Read<Phase4JReport>(Path.Combine(AppContext.BaseDirectory,"Research","phase4j-state.json"));
            Phase4JProjection.Validate(r,parent.Read());return r;
        }
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {throw new ApiFault(503,"DATA_NOT_READY");}
    }
}
