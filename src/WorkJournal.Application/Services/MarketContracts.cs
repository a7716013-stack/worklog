using System.Text.Json;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public interface ITaiwanMarketRankingProvider
{
    Task<MarketSnapshot> GetAsync(CancellationToken ct);
}

public static class MarketProviderFailure
{
    public static bool IsExpected(Exception e,CancellationToken ct) => e is HttpRequestException or JsonException or InvalidDataException or FormatException || e is OperationCanceledException && !ct.IsCancellationRequested;
}
