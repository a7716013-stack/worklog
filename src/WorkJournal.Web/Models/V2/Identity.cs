namespace WorkJournal.Web.Models.V2;

public sealed class Instrument
{
    public Guid InstrumentId { get; set; }
    public string PermanentKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string InstrumentType { get; set; } = "";
    public string Currency { get; set; } = "";
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public bool IsActive { get; set; }
}
// Intervals are half-open [ValidFrom, ValidTo). Symbols are not permanent identity.
public sealed class InstrumentSymbolHistory
{
    public Guid Id { get; set; }
    public Guid InstrumentId { get; set; }
    public string Market { get; set; } = "";
    public string Symbol { get; set; } = "";
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
}
public sealed class LegacyInstrumentMapping
{
    public Guid Id { get; set; }
    public Guid InstrumentId { get; set; }
    public string Source { get; set; } = "";
    public string Market { get; set; } = "";
    public string StockId { get; set; } = "";
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
}
public enum CorporateActionKind { Split, ReverseSplit, CashDividend, StockDividend, Merger, SpinOff, SymbolChange, Delisting }
public sealed class CorporateAction
{
    public Guid Id { get; set; }
    public Guid InstrumentId { get; set; }
    public CorporateActionKind Kind { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public DateTimeOffset? AvailableAt { get; set; }
    public string SourceReference { get; set; } = "";
}
