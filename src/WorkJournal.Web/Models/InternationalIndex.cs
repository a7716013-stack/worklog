namespace WorkJournal.Web.Models;

public record InternationalIndexDefinition(string Symbol, string Name, string Market)
{
    public string SourceUrl => "https://finance.yahoo.com/quote/" + Uri.EscapeDataString(Symbol) + "/";
}

public record InternationalIndexQuote(string Symbol, decimal? Value, decimal? PreviousClose,
    DateTimeOffset? QuotedAt, DateTimeOffset? RetrievedAt, bool IsStale, string Status)
{
    public decimal? Change => Value.HasValue && PreviousClose > 0 ? Value - PreviousClose : null;
    public decimal? ChangePercent => Change / PreviousClose * 100;
}

public record InternationalIndicesSnapshot(InternationalIndexQuote[] Quotes, DateTimeOffset CheckedAt);
