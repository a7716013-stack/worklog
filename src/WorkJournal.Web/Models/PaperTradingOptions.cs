namespace WorkJournal.Web.Models;
public class PaperTradingOptions
{
    public decimal InitialCash { get; set; } = 1000000m;
    public decimal CommissionRate { get; set; } = 0.001425m;
    public decimal CommissionDiscount { get; set; } = 1m;
    public decimal MinimumCommission { get; set; } = 20m;
    public decimal StockSellTaxRate { get; set; } = 0.003m;
    public decimal EtfSellTaxRate { get; set; } = 0.001m;
    public bool IsValid() => InitialCash > 0 && InitialCash <= 1000000000000m &&
        CommissionRate is >= 0 and <= 1 && CommissionDiscount is >= 0 and <= 1 &&
        MinimumCommission is >= 0 and <= 1000000 && StockSellTaxRate is >= 0 and <= 1 && EtfSellTaxRate is >= 0 and <= 1;
}
