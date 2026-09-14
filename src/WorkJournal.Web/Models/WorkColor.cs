using System.ComponentModel.DataAnnotations;
namespace WorkJournal.Web.Models;
public enum WorkColor
{
    [Display(Name = "綠色")] Green,
    [Display(Name = "藍色")] Blue,
    [Display(Name = "紫色")] Purple,
    [Display(Name = "橘色")] Orange,
    [Display(Name = "紅色")] Red,
    [Display(Name = "灰色")] Gray
}
