using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace WorkJournal.Web.Controllers;
[Authorize]
public sealed class ResearchValidationController : Controller
{
    [HttpGet] public IActionResult Index()=>View("~/Views/StockAnalysis/Validation.cshtml");
}
