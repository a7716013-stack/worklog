using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WorkJournal.Web.Models.V2;
namespace WorkJournal.Web.Data;

internal static partial class V2Mapping
{
    private static EntityTypeBuilder<T> Document<T>(ModelBuilder m, string table) where T : class, IFoundationDocument
    {
        var e = m.Entity<T>();
        e.ToTable(table, t => t.HasCheckConstraint("CK_" + table + "_Status", "[Status] IN (0,1,2)"));
        e.HasKey(x => x.Id);
        e.Property(x => x.Checksum).HasMaxLength(64).IsFixedLength();
        e.HasIndex(x => x.Checksum).IsUnique();
        e.Property(x => x.Status).IsConcurrencyToken();
        return e;
    }
    public static void ConfigureFoundations(ModelBuilder m)
    {
        var d = Document<DatasetSnapshot>(m, "V2DatasetSnapshots");
        d.Property(x => x.DatasetVersion).HasMaxLength(120);
        d.Property(x => x.SourceSummary).HasMaxLength(1000);
        d.HasMany(x => x.Records).WithOne().HasForeignKey(x => x.DatasetSnapshotId).OnDelete(DeleteBehavior.Restrict);
        var p = m.Entity<DataProvenance>();
        p.ToTable("V2DataProvenance", t => t.HasCheckConstraint("CK_V2Provenance_Quality", "[QualityStatus] BETWEEN 0 AND 4"));
        p.HasKey(x => x.Id);
        p.Property(x => x.Provider).HasMaxLength(120); p.Property(x => x.Dataset).HasMaxLength(120);
        p.Property(x => x.SourceKey).HasMaxLength(200); p.Property(x => x.RawReference).HasMaxLength(1000);
        p.Property(x => x.Checksum).HasMaxLength(64).IsFixedLength();
        p.HasIndex(x => new { x.DatasetSnapshotId, x.InstrumentId, x.AvailableAt });
        p.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        var u = Document<UniverseSnapshot>(m, "V2UniverseSnapshots");
        u.Property(x => x.UniverseVersion).HasMaxLength(120);
        u.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.UniverseSnapshotId).OnDelete(DeleteBehavior.Restrict);
        var member = m.Entity<UniverseSnapshotMember>();
        member.ToTable("V2UniverseMembers", t => t.HasCheckConstraint("CK_V2Universe_Eligibility", "[EligibilityStatus] BETWEEN 0 AND 2"));
        member.HasKey(x => new { x.UniverseSnapshotId, x.InstrumentId });
        member.Property(x => x.ReasonCode).HasMaxLength(200);
        member.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        var b = m.Entity<BenchmarkDefinition>(); b.ToTable("V2BenchmarkDefinitions"); b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(120); b.HasIndex(x => x.Code).IsUnique(); b.Property(x => x.Name).HasMaxLength(200);
        var bv = m.Entity<BenchmarkVersion>(); bv.ToTable("V2BenchmarkVersions", t => t.HasCheckConstraint("CK_V2Benchmark_Period", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]"));
        bv.HasKey(x => x.Id); bv.Property(x => x.Version).HasMaxLength(120);
        bv.HasIndex(x => new { x.BenchmarkDefinitionId, x.Version }).IsUnique();
        bv.HasOne<BenchmarkDefinition>().WithMany().HasForeignKey(x => x.BenchmarkDefinitionId).OnDelete(DeleteBehavior.Restrict);
        var bm = m.Entity<BenchmarkMapping>(); bm.ToTable("V2BenchmarkMappings", t => t.HasCheckConstraint("CK_V2BenchmarkMapping_Period", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]"));
        bm.HasKey(x => x.Id); bm.HasIndex(x => new { x.InstrumentId, x.ValidFrom }).IsUnique();
        bm.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        bm.HasOne<BenchmarkVersion>().WithMany().HasForeignKey(x => x.BenchmarkVersionId).OnDelete(DeleteBehavior.Restrict);
        var v = Document<VersionSet>(m, "V2VersionSets");
        foreach (var name in new[] { "ModelVersion", "FeatureVersion", "UniverseVersion", "DataVersion", "StrategyVersion" }) v.Property<string>(name).HasMaxLength(120);
        v.HasOne<BenchmarkVersion>().WithMany().HasForeignKey(x => x.BenchmarkVersionId).OnDelete(DeleteBehavior.Restrict);
        var param = Document<ParameterSet>(m, "V2ParameterSets");
        param.Property(x => x.Code).HasMaxLength(120); param.Property(x => x.Version).HasMaxLength(120);
        // Checksum is content-only; distinct named versions may intentionally contain the same values.
        param.HasIndex(x => x.Checksum).IsUnique(false);
        param.HasIndex(x => new { x.Code, x.Version }).IsUnique();
        var trace = Document<DecisionTrace>(m, "V2DecisionTraces");
        trace.Property(x => x.DecisionStatus).HasMaxLength(120); trace.Property(x => x.ReasonCode).HasMaxLength(200);
        trace.HasIndex(x => new { x.InstrumentId, x.TradeDate, x.DecisionType });
        trace.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        trace.HasOne<DatasetSnapshot>().WithMany().HasForeignKey(x => x.DatasetSnapshotId).OnDelete(DeleteBehavior.Restrict);
        trace.HasOne<UniverseSnapshot>().WithMany().HasForeignKey(x => x.UniverseSnapshotId).OnDelete(DeleteBehavior.Restrict);
        trace.HasOne<VersionSet>().WithMany().HasForeignKey(x => x.VersionSetId).OnDelete(DeleteBehavior.Restrict);
        trace.HasOne<ParameterSet>().WithMany().HasForeignKey(x => x.ParameterSetId).OnDelete(DeleteBehavior.Restrict);
        // All V2 persisted instants normalize to UTC, independent of the caller offset.
        var utc = new ValueConverter<DateTimeOffset, DateTimeOffset>(x => x.ToUniversalTime(), x => x.ToUniversalTime());
        foreach (var type in m.Model.GetEntityTypes().Where(x => x.ClrType.Namespace == typeof(Instrument).Namespace))
            foreach (var property in type.GetProperties().Where(x => x.ClrType == typeof(DateTimeOffset) || x.ClrType == typeof(DateTimeOffset?)))
                property.SetValueConverter(utc);
    }
    public static void Protect(JournalDbContext db)
    {
        // Check persisted truth, not OriginalValues (which can be forged by attaching a detached entity).
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State is EntityState.Modified or EntityState.Deleted).ToArray())
        {
            var frozen = entry.Entity switch
            {
                DatasetSnapshot x => db.Set<DatasetSnapshot>().AsNoTracking().Any(y => y.Id == x.Id && y.Status == FoundationStatus.Finalized),
                UniverseSnapshot x => db.Set<UniverseSnapshot>().AsNoTracking().Any(y => y.Id == x.Id && y.Status == FoundationStatus.Finalized),
                VersionSet x => db.Set<VersionSet>().AsNoTracking().Any(y => y.Id == x.Id && y.Status == FoundationStatus.Finalized),
                ParameterSet x => db.Set<ParameterSet>().AsNoTracking().Any(y => y.Id == x.Id && y.Status == FoundationStatus.Finalized),
                DecisionTrace x => db.Set<DecisionTrace>().AsNoTracking().Any(y => y.Id == x.Id && y.Status == FoundationStatus.Finalized),
                BenchmarkVersion or BenchmarkDefinition or BenchmarkMapping => true,
                _ => false
            };
            if (frozen) throw new InvalidOperationException("V2 finalized content is immutable; create a new version.");
            if (entry.Entity is Instrument && entry.Property(nameof(Instrument.PermanentKey)).IsModified)
                throw new InvalidOperationException("Permanent identity cannot change.");
        }
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray())
        {
            if (entry.Entity is DataProvenance p)
            {
                var persistedParent = entry.State == EntityState.Added ? p.DatasetSnapshotId : db.Set<DataProvenance>().AsNoTracking().Where(x => x.Id == p.Id).Select(x => x.DatasetSnapshotId).Single();
                var ids = new[] { p.DatasetSnapshotId, persistedParent };
                if (db.Set<DatasetSnapshot>().AsNoTracking().Any(x => ids.Contains(x.Id) && x.Status == FoundationStatus.Finalized))
                    throw new InvalidOperationException("Finalized snapshot records are immutable.");
            }
            if (entry.Entity is UniverseSnapshotMember member)
            {
                if (db.Set<UniverseSnapshot>().AsNoTracking().Any(x => x.Id == member.UniverseSnapshotId && x.Status == FoundationStatus.Finalized))
                    throw new InvalidOperationException("Finalized universe members are immutable.");
            }
        }
    }
}
