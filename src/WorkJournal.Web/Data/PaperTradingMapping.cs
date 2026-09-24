using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Data;
public static class PaperTradingMapping
{
    public static void Configure(ModelBuilder model)
    {
        var account = model.Entity<PaperTradingAccount>();
        account.Property(x => x.Id).ValueGeneratedNever();
        account.Property(x => x.Name).HasMaxLength(80);
        account.ToTable("PaperTradingAccounts", t => {
            t.HasCheckConstraint("CK_PaperAccount_Cash", "[Cash] >= 0 AND [InitialCash] > 0");
            t.HasCheckConstraint("CK_PaperAccount_Singleton", "[Id] = 1");
        });
        var order = model.Entity<PaperOrder>();
        order.Property(x => x.StockId).HasMaxLength(6);
        order.Property(x => x.StockName).HasMaxLength(120);
        order.Property(x => x.RejectReason).HasMaxLength(300);
        order.HasOne<PaperTradingAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        order.HasIndex(x => new { x.AccountId, x.ClientRequestId }).IsUnique();
        order.HasIndex(x => new { x.AccountId, x.Status, x.CreatedAt });
        order.ToTable("PaperOrders", t => {
            t.HasCheckConstraint("CK_PaperOrder_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_PaperOrder_Enums", "[Side] IN (0,1) AND [OrderType] IN (0,1) AND [Status] IN (0,1,2,3)");
            t.HasCheckConstraint("CK_PaperOrder_Limit", "([OrderType] = 0 AND [LimitPrice] IS NULL) OR ([OrderType] = 1 AND [LimitPrice] > 0)");
        });
        var trade = model.Entity<PaperTrade>();
        trade.Property(x => x.StockId).HasMaxLength(6);
        trade.Property(x => x.StockName).HasMaxLength(120);
        trade.HasOne<PaperTradingAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        trade.HasOne<PaperOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        // One execution in v1; sequence allows a future partial-fill model.
        trade.HasIndex(x => new { x.OrderId, x.ExecutionSequence }).IsUnique();
        trade.HasIndex(x => new { x.AccountId, x.ExecutedAt });
        trade.ToTable("PaperTrades", t => t.HasCheckConstraint("CK_PaperTrade_Amounts", "[Quantity] > 0 AND [Price] > 0 AND [Commission] >= 0 AND [TransactionTax] >= 0"));
        var position = model.Entity<PaperPosition>();
        position.Property(x => x.StockId).HasMaxLength(6);
        position.Property(x => x.StockName).HasMaxLength(120);
        position.HasOne<PaperTradingAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        position.HasIndex(x => new { x.AccountId, x.StockId }).IsUnique();
        position.ToTable("PaperPositions", t => t.HasCheckConstraint("CK_PaperPosition_Quantity", "[Quantity] >= 0 AND [CostBasis] >= 0 AND [AverageCost] >= 0"));
        foreach (var type in new[] { typeof(PaperTradingAccount), typeof(PaperOrder), typeof(PaperTrade), typeof(PaperPosition) })
            foreach (var property in model.Entity(type).Metadata.GetProperties())
                if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(decimal))
                { property.SetPrecision(28); property.SetScale(10); }
    }
}
