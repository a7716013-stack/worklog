namespace WorkJournal.Web.Models;
public class PaperTradingAccount
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "我的虛擬帳戶";
    public decimal InitialCash { get; set; }
    public decimal Cash { get; set; }
    public decimal RealizedProfitLoss { get; set; }
    public Guid Generation { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
