using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Models.V2;
namespace WorkJournal.Web.Data;

internal static partial class V2Mapping
{
    public static void ConfigureIdentity(ModelBuilder model)
    {
        var i = model.Entity<Instrument>();
        i.ToTable("V2Instruments", t => t.HasCheckConstraint("CK_V2Instrument_Period", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]"));
        i.HasKey(x => x.InstrumentId);
        i.Property(x => x.PermanentKey).HasMaxLength(200);
        i.HasIndex(x => x.PermanentKey).IsUnique();
        i.Property(x => x.Name).HasMaxLength(200);
        i.Property(x => x.InstrumentType).HasMaxLength(40);
        i.Property(x => x.Currency).HasMaxLength(3);
        var h = model.Entity<InstrumentSymbolHistory>();
        h.ToTable("V2InstrumentSymbols", t => t.HasCheckConstraint("CK_V2Symbol_Period", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]"));
        h.HasKey(x => x.Id);
        h.Property(x => x.Market).HasMaxLength(40);
        h.Property(x => x.Symbol).HasMaxLength(80);
        h.HasIndex(x => new { x.Market, x.Symbol, x.ValidFrom }).IsUnique();
        h.HasIndex(x => new { x.InstrumentId, x.ValidFrom }).IsUnique();
        h.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        var l = model.Entity<LegacyInstrumentMapping>();
        l.ToTable("V2LegacyInstrumentMappings", t => t.HasCheckConstraint("CK_V2Legacy_Period", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]"));
        l.HasKey(x => x.Id);
        l.Property(x => x.Source).HasMaxLength(80);
        l.Property(x => x.Market).HasMaxLength(40);
        l.Property(x => x.StockId).HasMaxLength(80);
        l.HasIndex(x => new { x.Source, x.Market, x.StockId, x.ValidFrom }).IsUnique();
        l.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        var c = model.Entity<CorporateAction>();
        c.ToTable("V2CorporateActions");
        c.HasKey(x => x.Id);
        c.Property(x => x.SourceReference).HasMaxLength(400);
        c.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        c.HasIndex(x => new { x.InstrumentId, x.EffectiveAt });
    }
}
