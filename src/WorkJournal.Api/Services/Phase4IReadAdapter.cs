using WorkJournal.Application.Research;
namespace WorkJournal.Api.Services;
public sealed class Phase4IReadAdapter(Phase4IReadService reports,ValidationReadAdapter validation)
{
    public Phase4IReport Read()
    {
        try
        {
            var r=reports.Read();
            if(r.Validation.Digest!=validation.Read().Digest)throw new InvalidDataException("Publication snapshots disagree");
            return r;
        }
        catch(Exception e) when(e is IOException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {throw new ApiFault(503,"DATA_NOT_READY");}
    }
}
