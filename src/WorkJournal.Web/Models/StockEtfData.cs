namespace WorkJournal.Web.Models;
public class EtfData
{
    public DateOnly? NavDate { get; set; }
    public decimal? Nav { get; set; }
    public decimal? Premium { get; set; }
    public decimal? Assets { get; set; }
    public DateOnly? AssetsDate { get; set; }
    public DateOnly? HoldingsDate { get; set; }
    public string? HoldingsUrl { get; set; }
    public List<EtfHolding> Holdings { get; set; } = [];
    public List<EtfDistribution> Distributions { get; set; } = [];
    public string DistributionMessage { get; set; } = "近三年無可用配息／配股紀錄。";
}
public record EtfHolding(string Symbol, string Name, decimal Weight);
public record EtfDistribution(DateOnly Date, decimal? Cash, decimal? Stock, string? PaymentDate);
