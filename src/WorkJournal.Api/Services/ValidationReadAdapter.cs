using WorkJournal.Application.Research;
namespace WorkJournal.Api.Services;
public sealed class ValidationReadAdapter(EvidenceValidationReadService reports)
{
    public EvidenceValidationReport Read()
    {
        try{return reports.Read();}
        catch(Exception e) when(e is IOException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {throw new ApiFault(503,"DATA_NOT_READY");}
    }
}
