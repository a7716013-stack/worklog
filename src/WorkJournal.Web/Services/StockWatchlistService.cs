using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Security;
namespace WorkJournal.Web.Services;

public class StockWatchlistService(JournalDbContext db, ICurrentUser user)
{
    public Task<List<SwingStock>> GetAsync(CancellationToken ct) => db.StockWatchlistItems.AsNoTracking()
        .Where(x => x.ApplicationUserId == user.Id).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
        .Select(x => new SwingStock(x.Symbol,x.Name,x.Market,x.IsEtf)).ToListAsync(ct);
    public async Task<List<SwingStock>> ChangeAsync(IReadOnlyList<SwingStock> additions, string? remove, CancellationToken ct)
    {
        var id = user.Id;
        if (additions.Count > 20) throw new ValidationException("最多追蹤 20 檔股票。");
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var key = "WorkJournal.Watchlist." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)));
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={key}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @result < 0 THROW 51001, 'Watchlist is busy.', 1;
                """, ct);
            var items = await db.StockWatchlistItems.Where(x => x.ApplicationUserId == id).ToListAsync(ct);
            if (remove is not null)
            {
                var item = items.SingleOrDefault(x => x.Symbol == remove);
                if (item is not null) { db.Remove(item); items.Remove(item); }
            }
            foreach (var stock in additions.DistinctBy(x => x.Symbol))
            {
                if (items.Any(x => x.Symbol == stock.Symbol)) continue;
                if (items.Count >= 20) throw new ValidationException("最多追蹤 20 檔；請先移除不需要的股票。");
                var item = new StockWatchlistItem { ApplicationUserId=id, Symbol=stock.Symbol, Name=stock.Name, Market=stock.Market, IsEtf=stock.IsEtf };
                items.Add(item); db.Add(item);
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return await GetAsync(ct);
        });
    }
}
