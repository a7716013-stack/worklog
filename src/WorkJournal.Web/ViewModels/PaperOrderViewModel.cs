using System.ComponentModel.DataAnnotations;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.ViewModels;
public class PaperOrderViewModel : IValidatableObject
{
    [Required, StringLength(6), RegularExpression(@"[0-9]{4}[0-9A-Z]{0,2}", ErrorMessage = "請輸入有效的台股代號。"), Display(Name = "股票代號")]
    public string StockId { get; set; } = "";
    [EnumDataType(typeof(PaperOrderSide)), Display(Name = "方向")]
    public PaperOrderSide Side { get; set; }
    [EnumDataType(typeof(PaperOrderType)), Display(Name = "委託類型")]
    public PaperOrderType OrderType { get; set; }
    [Range(1, 100000000, ErrorMessage = "股數須介於 1 至 100,000,000 股。"), Display(Name = "股數（支援零股）")]
    public int Quantity { get; set; } = 1000;
    [Range(typeof(decimal), "0.0001", "1000000", ErrorMessage = "限價須大於零且不超過 1,000,000。"), Display(Name = "限價（元）")]
    public decimal? LimitPrice { get; set; }
    public Guid ClientRequestId { get; set; } = Guid.NewGuid();
    public Guid AccountGeneration { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (OrderType == PaperOrderType.Limit && LimitPrice is null)
            yield return new("限價委託請填寫限價。", [nameof(LimitPrice)]);
        if (OrderType == PaperOrderType.Market && LimitPrice is not null)
            yield return new("市價委託不可指定限價。", [nameof(LimitPrice)]);
        if (ClientRequestId == Guid.Empty || AccountGeneration == Guid.Empty)
            yield return new("表單已失效，請重新整理頁面。");
    }
}
