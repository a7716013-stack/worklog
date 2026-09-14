using System.ComponentModel.DataAnnotations;
using WorkJournal.Web.Models;

namespace WorkJournal.Web.ViewModels;

public class StockAnalysisViewModel
{
    [Display(Name = "股票代號")]
    [StringLength(20, ErrorMessage = "股票代號最多 20 字。")]
    [RegularExpression(@"[A-Za-z0-9][A-Za-z0-9.\-^:]{0,19}", ErrorMessage = "請輸入英文字母、數字或有效的代號符號（. - ^ :）。")]
    public string? Symbol { get; set; }

    public StockQuote Quote { get; set; } = new();
    public string Message { get; set; } = "輸入股票代號，預覽行情欄位與分析區塊。";
}
