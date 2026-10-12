using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public class PortfolioAnalyticsService(JournalDbContext db,IMarketRadarService market,TimeProvider clock)
{
 public async Task CaptureAsync(int accountId,CancellationToken ct)
 {
  var now=clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8));var day=DateOnly.FromDateTime(now.DateTime);
  if(now.TimeOfDay<new TimeSpan(13,40,0)||now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)return;
  var quotes=(await market.GetAsync("all",day,false,ct)).Quotes.Where(x=>x.Date==day).ToArray();
  if(!new[]{"twse","tpex"}.All(m=>quotes.Any(x=>x.Stock.Market==m)))return;
  var owner=await db.PaperTradingAccounts.AsNoTracking().Where(x=>x.Id==accountId).Select(x=>x.ApplicationUserId).SingleOrDefaultAsync(ct);if(owner==null)return;
  await db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>{
   db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(ct);
   var key="WorkJournal.PaperTrading."+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(owner)));
   await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={key},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 51005,'Portfolio busy',1;",ct);
   var account=await db.PaperTradingAccounts.SingleOrDefaultAsync(x=>x.Id==accountId&&x.ApplicationUserId==owner,ct);if(account==null)return;
   var positions=await db.PaperPositions.AsNoTracking().Where(x=>x.AccountId==accountId&&x.Quantity>0).ToListAsync(ct);decimal value=0;
   foreach(var p in positions){var q=quotes.SingleOrDefault(x=>x.Stock.Symbol==p.StockId);if(q==null||q.Close<=0)return;value+=p.Quantity*q.Close;}
   var row=await db.PortfolioDailyValues.SingleOrDefaultAsync(x=>x.AccountId==accountId&&x.Generation==account.Generation&&x.TradeDate==day,ct);
   if(row==null){row=new(){AccountId=accountId,Generation=account.Generation,TradeDate=day};db.Add(row);}
   row.Cash=account.Cash;row.MarketValue=value;row.TotalAssets=value+account.Cash;row.InitialCash=account.InitialCash;row.CapturedAt=clock.GetUtcNow();
   await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
  });
 }
 public async Task<object> HistoryAsync(string owner,CancellationToken ct)
 {
  var account=await db.PaperTradingAccounts.AsNoTracking().SingleOrDefaultAsync(x=>x.ApplicationUserId==owner,ct);
  if(account==null)return new{points=Array.Empty<PortfolioDailyValue>(),maxDrawdown=(decimal?)null};
  var points=await db.PortfolioDailyValues.AsNoTracking().Where(x=>x.AccountId==account.Id&&x.Generation==account.Generation).OrderBy(x=>x.TradeDate).ToListAsync(ct);
  return new{points,maxDrawdown=PortfolioAnalyticsCalculator.Drawdown(points)};
 }
}
