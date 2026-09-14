using System.ComponentModel.DataAnnotations;
using WorkJournal.Web.Models;

namespace WorkJournal.Web.ViewModels;

public class StockAnalysisViewModel
{
    [Display(Name = "台股代號")]
    [StringLength(6, ErrorMessage = "請輸入 4 至 6 碼台股代號。")]
    [RegularExpression(@"[0-9]{4}[0-9A-Z]{0,2}", ErrorMessage = "請輸入台股代號，例如 2330、0050、00679B。")]
    public string? Symbol { get; set; }
    public StockQuote Quote { get; set; } = new();
    public string Message { get; set; } = "輸入台股代號，查詢最新可用日行情。";
}
